using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotivCellScopeApiServices.Models;
using static MotivCellScopeApiServices.Controllers.IncidentsController;

namespace MotivCellScopeApiServices.Controllers;
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "engineer,operator,admin")]
[IncidentsController.ValidationErrors]
public sealed class ReportsController : ControllerBase
{
    private readonly MotivReportDbContext _db;
    public ReportsController(MotivReportDbContext db) => _db = db;
    [HttpGet("incidents-summary")]
    public IActionResult IncidentSummary([FromQuery] ReportQuery query, [FromQuery] string? groupBy = null) => Handle(() =>
    {
        var filter = ValidateQuery(query.ToIncidentQuery());
        if (groupBy is not null)
            ValidateGroup(groupBy);
        using var connection = OpenConnection(_db);
        var rows = ReadRows(connection, filter);
        return Ok(new SummaryDto { From = filter.From, To = filter.To, Total = rows.Count, KnownDowntimeSeconds = KnownDowntime(rows), ExcludedDowntimeCount = rows.LongCount(i => !i.CommonProstoySeconds.HasValue), ByCategory = Group(rows, "category"), BySeverity = Group(rows, "severity"), ByDirection = Group(rows, "direction"), ByTechCenter = Group(rows, "techCenter"), ByAlarmType = Group(rows, "alarmType"), GroupBy = groupBy, Groups = groupBy is null ? null : Group(rows, groupBy) });
    });
    [HttpGet("incidents-export")]
    public IActionResult IncidentSummaryExport([FromQuery] ReportQuery query, [FromQuery] string groupBy = "direction") => Handle(() =>
    {
        ValidateGroup(groupBy);
        var filter = ValidateQuery(query.ToIncidentQuery());
        using var connection = OpenConnection(_db);
        var csv = new StringBuilder();
        CsvRow(csv, ["groupBy", "key", "name", "count", "knownDowntimeSeconds", "excludedDowntimeCount"]);
        foreach (var row in Group(ReadRows(connection, filter), groupBy))
            CsvRow(csv, [groupBy, row.Key, row.Name, row.Count.ToString(CultureInfo.InvariantCulture), row.KnownDowntimeSeconds.ToString(CultureInfo.InvariantCulture), row.ExcludedDowntimeCount.ToString(CultureInfo.InvariantCulture)]);
        return File(CsvBytes(csv), "text/csv; charset=utf-8", "incidents-summary.csv");
    });
    // Compatibility routes use the same incident DTO, filters, and CSV contract.
    [HttpGet("events")]
    public IActionResult Events([FromQuery] ReportQuery query) => Handle(() =>
    {
        var filter = ValidateQuery(query.ToIncidentQuery());
        using var connection = OpenConnection(_db);
        var page = ReadPage(connection, filter);
        return Ok(new ReportPageDto(page.Items.Select(ReportIncidentDto.FromIncident).ToList(), page.Total, page.Page, page.PageSize, page.From, page.To));
    });
    [HttpGet("export")]
    public IActionResult Export([FromQuery] ReportQuery query) => Handle(() =>
    {
        var filter = ValidateQuery(query.ToIncidentQuery());
        using var connection = OpenConnection(_db);
        return File(IncidentCsv(ReadRows(connection, filter)), "text/csv; charset=utf-8", "incidents.csv");
    });
    [HttpGet("available-periods")]
    public IActionResult AvailablePeriods() => Handle(() =>
    {
        using var connection = OpenConnection(_db);
        DateTime? from = null, to = null, maxBeginTime = null;
        long total = 0;
        using (var command = Command(connection, "SELECT MIN(begin_time),MAX(COALESCE(end_time,UTC_TIMESTAMP())),COUNT(*),MAX(begin_time) FROM incidents"))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                from = reader.IsDBNull(0) ? null : Utc(reader.GetDateTime(0));
                to = reader.IsDBNull(1) ? null : Utc(reader.GetDateTime(1));
                total = reader.GetInt64(2);
                maxBeginTime = reader.IsDBNull(3) ? null : Utc(reader.GetDateTime(3));
            }
        }

        var months = new List<MonthDto>();
        using (var command = Command(connection, "SELECT YEAR(begin_time),MONTH(begin_time),COUNT(*) FROM incidents GROUP BY YEAR(begin_time),MONTH(begin_time) ORDER BY YEAR(begin_time) DESC,MONTH(begin_time) DESC"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var year = reader.GetInt32(0);
                var month = reader.GetInt32(1);
                var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                // DATETIME's maximum year has no representable following month.
                var end = year == 9999 && month == 12 ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : start.AddMonths(1);
                months.Add(new MonthDto(year, month, start, end, reader.GetInt64(2)));
            }
        }

        return Ok(new AvailablePeriodsDto(from, to, total, months, from, maxBeginTime));
    });
    private static List<GroupDto> Group(List<IncidentDto> rows, string groupBy)
    {
        static string? Id(ushort? value) => value?.ToString(CultureInfo.InvariantCulture);
        (string? Key, string? Name) Key(IncidentDto row) => groupBy switch
        {
            "category" => (row.Category, row.Category),
            "severity" => (row.Severity, row.Severity),
            "direction" => (Id(row.DirectionId), row.DirectionName ?? row.DirectionCode),
            "techCenter" => (Id(row.TechCenterId), row.TechCenterName ?? row.TechCenterCode),
            "alarmType" => (Id(row.AlarmTypeId), row.AlarmTypeName ?? row.AlarmTypeCode),
            _ => throw Invalid("groupBy", "Unknown grouping.")};
        return rows.GroupBy(Key).Select(group => new GroupDto(group.Key.Key, group.Key.Name, group.LongCount(), KnownDowntime(group), group.LongCount(i => !i.CommonProstoySeconds.HasValue))).OrderByDescending(g => g.Count).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
    }

    private static decimal KnownDowntime(IEnumerable<IncidentDto> rows) => rows.Sum(i => (decimal)(i.CommonProstoySeconds ?? 0));
    private static void ValidateGroup(string groupBy)
    {
        if (groupBy is not ("category" or "severity" or "direction" or "techCenter" or "alarmType"))
            throw Invalid("groupBy", "Allowed values: category, severity, direction, techCenter, alarmType.");
    }

    public sealed class ReportQuery
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public ushort? DirectionId { get; set; }
        public ushort? TechCenterId { get; set; }
        public ushort? PriorityId { get; set; }
        public ushort? NetworkTypeId { get; set; }
        public ushort? EquipmentTypeId { get; set; }
        public ushort? AlarmTypeId { get; set; }
        public ushort? RegionId { get; set; }
        public string? Category { get; set; }
        public string? Severity { get; set; }
        public string Status { get; set; } = "all";
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string Sort { get; set; } = "begin_time";
        public string Order { get; set; } = "desc";

        internal IncidentQuery ToIncidentQuery() => new()
        {
            From = From,
            To = To,
            DirectionId = DirectionId,
            TechCenterId = TechCenterId,
            PriorityId = PriorityId,
            NetworkTypeId = NetworkTypeId,
            EquipmentTypeId = EquipmentTypeId,
            AlarmTypeId = AlarmTypeId,
            RegionId = RegionId,
            Category = Category,
            Severity = Severity,
            Status = Status,
            Search = Search,
            Page = Page,
            PageSize = PageSize,
            Sort = Sort,
            Order = Order
        };
    }

    public sealed class ReportIncidentDto
    {
        public ulong IncidentId { get; set; }
        public string? Pw { get; set; }
        public ushort? DirectionId { get; set; }
        public string? DirectionCode { get; set; }
        public string? DirectionName { get; set; }
        public ushort? TechCenterId { get; set; }
        public string? TechCenterCode { get; set; }
        public string? TechCenterName { get; set; }
        public ushort? PriorityId { get; set; }
        public string? PriorityCode { get; set; }
        public string? PriorityName { get; set; }
        public ushort? NetworkTypeId { get; set; }
        public string? NetworkTypeCode { get; set; }
        public string? NetworkTypeName { get; set; }
        public string? ServElem { get; set; }
        public string? NumbRbs { get; set; }
        public ushort? EquipmentTypeId { get; set; }
        public string? EquipmentTypeCode { get; set; }
        public string? EquipmentTypeName { get; set; }
        public ushort? AlarmTypeId { get; set; }
        public string? AlarmTypeCode { get; set; }
        public string? AlarmTypeName { get; set; }
        public string? Descr { get; set; }
        public DateTime BeginTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Akb { get; set; }
        public string? Dgu { get; set; }
        public string Lenght { get; set; } = "";
        public string? Prostoy { get; set; }
        public string? CommonProstoy { get; set; }
        public string? AlarmWho { get; set; }
        public string? DescNotice { get; set; }
        public string? AdminDistrict { get; set; }
        public uint? NetworkElementId { get; set; }
        public string? NetworkElementName { get; set; }
        public ushort? RegionId { get; set; }
        public string? RegionCode { get; set; }
        public string? RegionName { get; set; }
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Status { get; set; } = "";
        public uint? CreatedByUserId { get; set; }
        public string? CreatedByUsername { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public long DurationSeconds { get; set; }
        public long? CommonProstoySeconds { get; set; }

        internal static ReportIncidentDto FromIncident(IncidentDto value) => new()
        {
            IncidentId = value.IncidentId,
            Pw = value.Pw,
            DirectionId = value.DirectionId,
            DirectionCode = value.DirectionCode,
            DirectionName = value.DirectionName,
            TechCenterId = value.TechCenterId,
            TechCenterCode = value.TechCenterCode,
            TechCenterName = value.TechCenterName,
            PriorityId = value.PriorityId,
            PriorityCode = value.PriorityCode,
            PriorityName = value.PriorityName,
            NetworkTypeId = value.NetworkTypeId,
            NetworkTypeCode = value.NetworkTypeCode,
            NetworkTypeName = value.NetworkTypeName,
            ServElem = value.ServElem,
            NumbRbs = value.NumbRbs,
            EquipmentTypeId = value.EquipmentTypeId,
            EquipmentTypeCode = value.EquipmentTypeCode,
            EquipmentTypeName = value.EquipmentTypeName,
            AlarmTypeId = value.AlarmTypeId,
            AlarmTypeCode = value.AlarmTypeCode,
            AlarmTypeName = value.AlarmTypeName,
            Descr = value.Descr,
            BeginTime = value.BeginTime,
            EndTime = value.EndTime,
            Akb = value.Akb,
            Dgu = value.Dgu,
            Lenght = value.Lenght,
            Prostoy = value.Prostoy,
            CommonProstoy = value.CommonProstoy,
            AlarmWho = value.AlarmWho,
            DescNotice = value.DescNotice,
            AdminDistrict = value.AdminDistrict,
            NetworkElementId = value.NetworkElementId,
            NetworkElementName = value.NetworkElementName,
            RegionId = value.RegionId,
            RegionCode = value.RegionCode,
            RegionName = value.RegionName,
            Severity = value.Severity,
            Category = value.Category,
            Status = value.Status,
            CreatedByUserId = value.CreatedByUserId,
            CreatedByUsername = value.CreatedByUsername,
            CreatedAt = value.CreatedAt,
            UpdatedAt = value.UpdatedAt,
            DurationSeconds = value.DurationSeconds,
            CommonProstoySeconds = value.CommonProstoySeconds
        };
    }

    public sealed record ReportPageDto(List<ReportIncidentDto> Items, long Total, int Page, int PageSize, DateTime From, DateTime To);
    public sealed class SummaryDto
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public long Total { get; set; }
        public decimal KnownDowntimeSeconds { get; set; }
        public long ExcludedDowntimeCount { get; set; }
        public List<GroupDto> ByCategory { get; set; } = [];
        public List<GroupDto> BySeverity { get; set; } = [];
        public List<GroupDto> ByDirection { get; set; } = [];
        public List<GroupDto> ByTechCenter { get; set; } = [];
        public List<GroupDto> ByAlarmType { get; set; } = [];
        public string? GroupBy { get; set; }
        public List<GroupDto>? Groups { get; set; }
    }

    public sealed record GroupDto(string? Key, string? Name, long Count, decimal KnownDowntimeSeconds, long ExcludedDowntimeCount);
    public sealed record AvailablePeriodsDto(DateTime? From, DateTime? To, long Total, List<MonthDto> Months, DateTime? MinBeginTime, DateTime? MaxBeginTime);
    public sealed record MonthDto(int Year, int Month, DateTime From, DateTime To, long Count);
}
