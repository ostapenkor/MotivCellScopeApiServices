using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Controllers;
/// <summary>Read-only monitoring and role-protected aliases for existing maintenance operations.</summary>
[ApiController]
[Authorize]
[Route("api/monitoring")]
public sealed class MonitoringController : ControllerBase
{
    private readonly MotivReportDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MonitoringController> _logger;
    private static readonly string[] ElementTypes =
    {
        "bts",
        "ip_node",
        "optical_node",
        "dwdm_node",
        "rrl_node",
        "opts_node",
        "sss_node",
        "client_channel",
        "mgmn_node"
    };
    private static readonly string[] ElementStatuses =
    {
        "active",
        "degraded",
        "down",
        "maintenance",
        "decommissioned"
    };
    private static readonly string[] AnomalyTypes =
    {
        "threshold",
        "zscore",
        "missing_data",
        "logical",
        "cross_source"
    };
    private static readonly string[] AnomalyStatuses =
    {
        "new",
        "acknowledged",
        "explained",
        "resolved",
        "false_positive"
    };
    private static readonly string[] Severities =
    {
        "info",
        "warning",
        "critical"
    };
    private static readonly string[] KpiCodes =
    {
        "cssr",
        "dcr",
        "availability",
        "traffic_dl",
        "traffic_ul"
    };
    private const string CellJoin = " FROM cells c JOIN bts b ON b.bts_id=c.bts_id JOIN network_elements ne ON ne.id=b.bts_id ";
    private const string AnomalyJoin = " FROM anomalies a LEFT JOIN cells c ON c.cell_id=a.cell_id LEFT JOIN network_elements ne ON ne.id=c.bts_id ";
    public MonitoringController(MotivReportDbContext db, IConfiguration configuration, ILogger<MonitoringController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("summary")]
    [HttpGet("/api/dashboard/summary")]
    public IActionResult Summary([FromQuery] MonitoringQuery query) => Read(connection =>
    {
        var filter = Parse(query);
        var args = Parameters(filter);
        var networkWhere = NetworkWhere(filter, args);
        var statusRows = Rows(connection, "SELECT ne.status, COUNT(*) AS count FROM network_elements ne WHERE " + networkWhere + " GROUP BY ne.status", args);
        var statusCounts = ElementStatuses.ToDictionary(value => value, _ => 0L);
        foreach (var row in statusRows)
            statusCounts[(string)row["status"]!] = Number(row, "count");
        var networkTotal = statusCounts.Values.Sum();
        var btsTotal = Scalar(connection, "SELECT COUNT(*) FROM bts b JOIN network_elements ne ON ne.id=b.bts_id WHERE " + networkWhere, args);
        var cellWhere = networkWhere + (filter.CellId.HasValue ? " AND c.cell_id=@cellId" : "");
        var cellTotal = Scalar(connection, "SELECT COUNT(*)" + CellJoin + "WHERE " + cellWhere, args);
        var kpis = Aggregate(connection, filter, cellWhere, args, cellTotal);
        // Count network elements independently of cells. v_network_snapshot joins cells and overcounts status sums.
        var anomalyWhere = AnomalyWhere(filter, args, includeAnomalyStatus: false) + " AND (a.cell_id IS NULL OR (" + networkWhere + "))";
        // Global anomalies have no network ownership and cannot match a scoped network filter.
        if (HasNetworkScope(filter))
            anomalyWhere += " AND a.cell_id IS NOT NULL";
        if (filter.CellId.HasValue)
            anomalyWhere += " AND a.cell_id=@cellId";
        var anomalyRows = Rows(connection, "SELECT a.status, a.severity, COUNT(*) AS count" + AnomalyJoin + "WHERE " + anomalyWhere + " GROUP BY a.status,a.severity", args);
        long anomaliesTotal = 0, openAnomalies = 0, criticalAnomalies = 0;
        var anomalyStatusCounts = AnomalyStatuses.ToDictionary(value => value, _ => 0L);
        var severityCounts = Severities.ToDictionary(value => value, _ => 0L);
        foreach (var row in anomalyRows)
        {
            var count = Number(row, "count");
            var status = (string)row["status"]!;
            var severity = (string)row["severity"]!;
            anomaliesTotal += count;
            anomalyStatusCounts[status] += count;
            severityCounts[severity] += count;
            if (status is "new" or "acknowledged")
                openAnomalies += count;
            if (severity == "critical" && (status is "new" or "acknowledged"))
                criticalAnomalies += count;
        }

        return Ok(new { from = filter.From, to = filter.To, networkElementsTotal = networkTotal, networkStatusCounts = statusCounts, btsTotal, cellTotal, anomaliesTotal, openAnomalies, criticalAnomalies, anomalyStatusCounts, severityCounts, avgCssr = kpis["cssrPct"], avgDcr = kpis["dcrPct"], avgAvailability = (decimal? )null, trafficDlGb = kpis["trafficDlGb"], trafficUlGb = kpis["trafficUlGb"], trafficTotalGb = kpis["trafficTotalGb"], trafficActivityPct = kpis["trafficActivityPct"], availabilityBasis = "traffic_activity_proxy", coverage = kpis["coverage"], kpis });
    });
    [HttpGet("network-elements")]
    public IActionResult NetworkElements([FromQuery] MonitoringQuery query) => NetworkList(query, btsOnly: false);
    [HttpGet("bts")]
    [HttpGet("/api/bts")]
    public IActionResult Bts([FromQuery] MonitoringQuery query) => NetworkList(query, btsOnly: true);
    private IActionResult NetworkList(MonitoringQuery query, bool btsOnly) => Read(connection =>
    {
        var filter = Parse(query);
        var args = Parameters(filter);
        var where = NetworkWhere(filter, args);
        if (btsOnly)
            where += " AND ne.element_type='bts' AND EXISTS(SELECT 1 FROM bts bx WHERE bx.bts_id=ne.id)";
        var total = Scalar(connection, "SELECT COUNT(*) FROM network_elements ne WHERE " + where, args);
        var page = query.ForMap ? 1 : filter.Page;
        var size = query.ForMap ? 10000 : filter.PageSize;
        args["@limit"] = size;
        args["@offset"] = (long)(page - 1) * size;
        var items = Rows(connection, NetworkSelect(filter, args) + " WHERE " + where + " ORDER BY ne.id LIMIT @limit OFFSET @offset", args);
        foreach (var item in items)
            SanitizeCoordinates(item);
        return Ok(new { items, total, page, pageSize = size, from = filter.From, to = filter.To, forMap = query.ForMap, truncated = query.ForMap && total > size, coordinatesExposed = ExposeCoordinates, coordinatesHidden = !ExposeCoordinates });
    });
    [HttpGet("network-elements/{id:long}")]
    public IActionResult NetworkElement(long id, [FromQuery] MonitoringQuery query) => NetworkDetail(id, query, btsOnly: false);
    [HttpGet("bts/{id:long}")]
    [HttpGet("/api/bts/{id:long}")]
    public IActionResult BtsDetail(long id, [FromQuery] MonitoringQuery query) => NetworkDetail(id, query, btsOnly: true);
    private IActionResult NetworkDetail(long id, MonitoringQuery query, bool btsOnly) => Read(connection =>
    {
        ValidateId(id);
        var filter = Parse(query);
        var args = Parameters(filter);
        args["@id"] = id;
        var exists = Scalar(connection, "SELECT COUNT(*) FROM network_elements ne WHERE ne.id=@id" + (btsOnly ? " AND ne.element_type='bts' AND EXISTS(SELECT 1 FROM bts bx WHERE bx.bts_id=ne.id)" : ""), args);
        if (exists == 0)
            return NotFound(new { message = "Network element not found." });
        var network = Rows(connection, NetworkSelect(filter, args) + " WHERE ne.id=@id", args).Single();
        SanitizeCoordinates(network);
        var cellCount = Scalar(connection, "SELECT COUNT(*) FROM cells WHERE bts_id=@id", args);
        var kpis = Aggregate(connection, filter, "ne.id=@id", args, cellCount);
        args["@limit"] = 10000;
        args["@offset"] = 0;
        var sectors = CellRows(connection, filter, "c.bts_id=@id", args);
        var anomalyWhere = AnomalyWhere(filter, args, includeAnomalyStatus: false) + " AND ne.id=@id";
        var anomalyTotal = Scalar(connection, "SELECT COUNT(*)" + AnomalyJoin + "WHERE " + anomalyWhere, args);
        args["@limit"] = 50;
        var anomalies = Rows(connection, AnomalySelect + " WHERE " + anomalyWhere + " ORDER BY a.ts_from DESC,a.anomaly_id DESC LIMIT @limit", args);
        var incidentTotal = Scalar(connection, "SELECT COUNT(*) FROM incidents i WHERE i.network_element_id=@id AND i.begin_time<@to AND (i.end_time IS NULL OR i.end_time>@from)", args);
        var incidents = Rows(connection, IncidentSelect + " WHERE i.network_element_id=@id AND i.begin_time<@to AND (i.end_time IS NULL OR i.end_time>@from) ORDER BY i.begin_time DESC,i.incident_id DESC LIMIT @limit", args);
        AddIncidentLinks(incidents);
        return Ok(new { from = filter.From, to = filter.To, networkElement = network, kpis, sectors, sectorsTotal = cellCount, sectorsTruncated = cellCount > 10000, anomalies, anomaliesTotal = anomalyTotal, anomaliesTruncated = anomalyTotal > 50, incidents, incidentsTotal = incidentTotal, incidentsTruncated = incidentTotal > 50 });
    });
    [HttpGet("cells")]
    [HttpGet("/api/cells")]
    public IActionResult Cells([FromQuery] MonitoringQuery query) => Read(connection =>
    {
        var filter = Parse(query);
        var args = Parameters(filter);
        var where = NetworkWhere(filter, args) + (filter.CellId.HasValue ? " AND c.cell_id=@cellId" : "");
        var total = Scalar(connection, "SELECT COUNT(*)" + CellJoin + "WHERE " + where, args);
        args["@limit"] = filter.PageSize;
        args["@offset"] = (long)(filter.Page - 1) * filter.PageSize;
        var items = CellRows(connection, filter, where, args);
        return Ok(new { items, total, page = filter.Page, pageSize = filter.PageSize, from = filter.From, to = filter.To });
    });
    [HttpGet("cells/{id:long}")]
    [HttpGet("/api/cells/{id:long}")]
    public IActionResult CellDetail(long id, [FromQuery] MonitoringQuery query) => Read(connection =>
    {
        ValidateId(id);
        var filter = Parse(query);
        var args = Parameters(filter);
        args["@id"] = id;
        if (Scalar(connection, "SELECT COUNT(*) FROM cells WHERE cell_id=@id", args) == 0)
            return NotFound(new { message = "Cell not found." });
        args["@limit"] = 1;
        args["@offset"] = 0;
        var cell = CellRows(connection, filter, "c.cell_id=@id", args).Single();
        var anomaliesWhere = AnomalyWhere(filter, args, includeAnomalyStatus: false) + " AND a.cell_id=@id";
        var anomalyTotal = Scalar(connection, "SELECT COUNT(*)" + AnomalyJoin + "WHERE " + anomaliesWhere, args);
        args["@limit"] = 50;
        var anomalies = Rows(connection, AnomalySelect + " WHERE " + anomaliesWhere + " ORDER BY a.ts_from DESC,a.anomaly_id DESC LIMIT @limit", args);
        var incidentTotal = Scalar(connection, "SELECT COUNT(*) FROM incidents i WHERE i.network_element_id=(SELECT bts_id FROM cells WHERE cell_id=@id) AND i.begin_time<@to AND (i.end_time IS NULL OR i.end_time>@from)", args);
        var incidents = Rows(connection, IncidentSelect + " WHERE i.network_element_id=(SELECT bts_id FROM cells WHERE cell_id=@id) AND i.begin_time<@to AND (i.end_time IS NULL OR i.end_time>@from) ORDER BY i.begin_time DESC,i.incident_id DESC LIMIT @limit", args);
        AddIncidentLinks(incidents);
        return Ok(new { from = filter.From, to = filter.To, cell, anomalies, anomaliesTotal = anomalyTotal, anomaliesTruncated = anomalyTotal > 50, incidents, incidentsTotal = incidentTotal, incidentsTruncated = incidentTotal > 50 });
    });
    [HttpGet("cells/{id:long}/kpi")]
    [HttpGet("/api/cells/{id:long}/kpi")]
    public IActionResult CellKpi(long id, [FromQuery] MonitoringQuery query) => Read(connection =>
    {
        ValidateId(id);
        var filter = Parse(query);
        var args = Parameters(filter);
        args["@id"] = id;
        if (Scalar(connection, "SELECT COUNT(*) FROM cells WHERE cell_id=@id", args) == 0)
            return NotFound(new { message = "Cell not found." });
        var total = Scalar(connection, "SELECT COUNT(*) FROM kpi_cell_hourly k WHERE k.cell_id=@id AND k.ts_hour>=@from AND k.ts_hour<@to", args);
        args["@limit"] = filter.PageSize;
        args["@offset"] = (long)(filter.Page - 1) * filter.PageSize;
        var items = Rows(connection, "SELECT k.ts_hour AS tsHour,k.conn_attempt AS connAttempt,k.conn_success AS connSuccess,k.conn_drop AS connDrop," + "k.traffic_dl_bytes AS trafficDlBytes,k.traffic_ul_bytes AS trafficUlBytes,k.computed_at AS computedAt FROM kpi_cell_hourly k " + "WHERE k.cell_id=@id AND k.ts_hour>=@from AND k.ts_hour<@to ORDER BY k.ts_hour LIMIT @limit OFFSET @offset", args);
        foreach (var item in items)
        {
            item["cssrPct"] = Ratio(Decimal(item, "connSuccess"), Decimal(item, "connAttempt"));
            item["dcrPct"] = Ratio(Decimal(item, "connDrop"), Decimal(item, "connSuccess"));
            item["trafficDlGb"] = Decimal(item, "trafficDlBytes") / 1000000000m;
            item["trafficUlGb"] = Decimal(item, "trafficUlBytes") / 1000000000m;
            item["trafficTotalGb"] = (Decimal(item, "trafficDlBytes") + Decimal(item, "trafficUlBytes")) / 1000000000m;
            item["availabilityPct"] = null;
            item["trafficActivityPct"] = Decimal(item, "connAttempt") > 0 || Decimal(item, "trafficDlBytes") > 0 || Decimal(item, "trafficUlBytes") > 0 ? 100m : 0m;
        }

        var aggregate = Aggregate(connection, filter, "c.cell_id=@id", args, 1);
        return Ok(new { cellId = id, from = filter.From, to = filter.To, items, total, page = filter.Page, pageSize = filter.PageSize, kpis = aggregate, missingHours = "Omitted hours are unknown; use coverage to assess missing data.", availabilityBasis = "traffic_activity_proxy" });
    });
    [HttpGet("anomalies")]
    [HttpGet("/api/anomalies")]
    public IActionResult Anomalies([FromQuery] MonitoringQuery query) => Read(connection =>
    {
        var filter = Parse(query, anomalyFilters: true);
        var args = Parameters(filter);
        var where = AnomalyWhere(filter, args);
        if (filter.RegionId.HasValue)
            where += " AND ne.region_id=@regionId";
        if (filter.BtsId.HasValue)
            where += " AND c.bts_id=@btsId";
        if (filter.CellId.HasValue)
            where += " AND a.cell_id=@cellId";
        if (filter.Search != null)
        {
            args["@search"] = "%" + EscapeLike(filter.Search) + "%";
            where += " AND (ne.code LIKE @search ESCAPE '=' OR ne.full_name LIKE @search ESCAPE '=')";
        }

        var total = Scalar(connection, "SELECT COUNT(*)" + AnomalyJoin + "WHERE " + where, args);
        args["@limit"] = filter.PageSize;
        args["@offset"] = (long)(filter.Page - 1) * filter.PageSize;
        var items = Rows(connection, AnomalySelect + " WHERE " + where + " ORDER BY a.ts_from DESC,a.anomaly_id DESC LIMIT @limit OFFSET @offset", args);
        return Ok(new { items, total, page = filter.Page, pageSize = filter.PageSize, from = filter.From, to = filter.To });
    });
    [HttpGet("anomalies/{id:long}")]
    [HttpGet("/api/anomalies/{id:long}")]
    public IActionResult AnomalyDetail(long id) => Read(connection =>
    {
        if (id <= 0)
            throw Invalid("id must be positive.");
        var args = new Dictionary<string, object?>
        {
            ["@id"] = id
        };
        var anomaly = Rows(connection, AnomalySelect + " WHERE a.anomaly_id=@id", args).SingleOrDefault();
        if (anomaly == null)
            return NotFound(new { message = "Anomaly not found." });
        var incidents = Rows(connection, IncidentSelect + " WHERE i.incident_id=(SELECT explained_by_incident FROM anomalies WHERE anomaly_id=@id)", args);
        AddIncidentLinks(incidents);
        return Ok(new { anomaly, incidents });
    });
    [Authorize(Roles = "operator,admin")]
    [HttpPost("detect-anomalies")]
    public IActionResult DetectAnomalies([FromBody] MonitoringDetectRequest request)
    {
        if (request.HoursBack is < 1 or > 8760)
            return BadRequest(new { message = "hoursBack must be between 1 and 8760." });
        return RunAdmin("detect_anomalies", () => ExistingAdmin().DetectAnomalies(new DetectAnomaliesRequest { HoursBack = request.HoursBack }));
    }

    [Authorize(Roles = "operator,admin")]
    [HttpPost("refresh-kpi")]
    public IActionResult RefreshKpi([FromBody] MonitoringPeriodRequest request)
    {
        try
        {
            var period = ParsePeriod(request.From, request.To, required: true);
            return RunAdmin("refresh_kpi", () => ExistingAdmin().RefreshKpi(new PeriodRequest { From = period.From.UtcDateTime, To = period.To.UtcDateTime }));
        }
        catch (MonitoringValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private AdminController ExistingAdmin() => new(_db)
    {
        ControllerContext = ControllerContext
    };
    private IActionResult RunAdmin(string action, Func<IActionResult> operation)
    {
        try
        {
            var result = operation();
            WriteAudit(action, success: true);
            return result;
        }
        catch
        {
            // The existing procedures remain owned by AdminController. Record a failed invocation when possible.
            foreach (var entry in _db.ChangeTracker.Entries<AuditLog>().Where(entry => entry.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            try
            {
                WriteAudit(action, success: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record monitoring operation {Action}", action);
            }

            throw;
        }
    }

    private void WriteAudit(string action, bool success)
    {
        var name = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "unknown";
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var idText = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        uint? userId = uint.TryParse(idText, out var parsed) ? parsed : null;
        _db.AuditLogs.Add(new AuditLog { Ts = DateTime.UtcNow, UserId = userId, Username = name, Role = role, Action = action, EntityType = action == "refresh_kpi" ? "kpi" : "anomaly", Success = success, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });
        _db.SaveChanges();
    }

    private bool ExposeCoordinates => _configuration.GetValue<bool>("Monitoring:ExposeCoordinates");

    private void SanitizeCoordinates(Dictionary<string, object?> item)
    {
        item["coordinatesHidden"] = !ExposeCoordinates;
        if (!ExposeCoordinates)
        {
            item["lat"] = null;
            item["lon"] = null;
            item["address"] = null;
        }
    }

    private static string NetworkSelect(Filter filter, Dictionary<string, object?> args)
    {
        var anomalyWhere = AnomalyWhere(filter, args, includeAnomalyStatus: false);
        return "SELECT ne.id,ne.element_type AS type,ne.code,ne.network_element_code AS networkElementCode," + "COALESCE(ne.full_name,ne.short_name,ne.code) AS name,ne.full_name AS fullName,ne.short_name AS shortName,ne.description," + "ne.status,ne.status_since AS statusSince,ne.stop_reason AS stopReason,ne.expected_restore_date AS expectedRestoreDate," + "ne.actual_restore_date AS actualRestoreDate,ne.commissioned_at AS commissionedAt,ne.decommissioned_at AS decommissionedAt," + "ne.region_id AS regionId,r.code AS regionCode,r.name AS regionName,ne.tech_center_id AS techCenterId," + "tc.code AS techCenterCode,tc.name AS techCenterName,d.direction_id AS directionId,d.code AS directionCode,d.name AS directionName," + "b.site_type AS siteType,b.lat,b.lon,b.address," + "(SELECT COUNT(*) FROM cells cc WHERE cc.bts_id=ne.id) AS cellCount," + "(SELECT COUNT(*) FROM anomalies a JOIN cells c ON c.cell_id=a.cell_id WHERE c.bts_id=ne.id AND " + anomalyWhere + ") AS anomalyCount," + "(SELECT COUNT(*) FROM anomalies a JOIN cells c ON c.cell_id=a.cell_id WHERE c.bts_id=ne.id AND a.status IN ('new','acknowledged') AND " + anomalyWhere + ") AS openAnomalyCount," + "(SELECT CASE MAX(CASE a.severity WHEN 'critical' THEN 3 WHEN 'warning' THEN 2 ELSE 1 END) WHEN 3 THEN 'critical' WHEN 2 THEN 'warning' WHEN 1 THEN 'info' ELSE NULL END FROM anomalies a JOIN cells c ON c.cell_id=a.cell_id WHERE c.bts_id=ne.id AND a.status IN ('new','acknowledged') AND " + anomalyWhere + ") AS anomalySeverity " + "FROM network_elements ne LEFT JOIN bts b ON b.bts_id=ne.id LEFT JOIN regions r ON r.region_id=ne.region_id " + "LEFT JOIN technical_centers tc ON tc.tech_center_id=ne.tech_center_id LEFT JOIN directions d ON d.direction_id=tc.direction_id";
    }

    private static string NetworkWhere(Filter filter, Dictionary<string, object?> args)
    {
        var where = "1=1";
        if (filter.RegionId.HasValue)
            where += " AND ne.region_id=@regionId";
        if (filter.BtsId.HasValue)
            where += " AND ne.id=@btsId";
        if (filter.CellId.HasValue)
            where += " AND EXISTS(SELECT 1 FROM cells scope_cell WHERE scope_cell.bts_id=ne.id AND scope_cell.cell_id=@cellId)";
        where += In("ne.status", "networkStatus", filter.Statuses, args);
        where += In("ne.element_type", "elementType", filter.Types, args);
        if (filter.Search != null)
        {
            args["@search"] = "%" + EscapeLike(filter.Search) + "%";
            where += " AND (ne.code LIKE @search ESCAPE '=' OR ne.full_name LIKE @search ESCAPE '=' OR ne.short_name LIKE @search ESCAPE '=')";
        }

        if (filter.Severities.Length > 0 || filter.KpiCodes.Length > 0)
            where += " AND EXISTS(SELECT 1 FROM anomalies a JOIN cells ac ON ac.cell_id=a.cell_id WHERE ac.bts_id=ne.id AND " + AnomalyWhere(filter, args, includeAnomalyStatus: false) + ")";
        return where;
    }

    private static bool HasNetworkScope(Filter filter) => filter.RegionId.HasValue || filter.BtsId.HasValue || filter.CellId.HasValue || filter.Statuses.Length > 0 || filter.Types.Length > 0 || filter.Search != null;
    private static string AnomalyWhere(Filter filter, Dictionary<string, object?> args, bool includeAnomalyStatus = true)
    {
        // Strict overlap for half-open periods, including instantaneous anomalies at ts_from.
        var where = "a.ts_from<@to AND (a.ts_to>@from OR (a.ts_from=a.ts_to AND a.ts_from>=@from))";
        where += In("a.severity", "severity", filter.Severities, args);
        where += In("a.kpi_code", "kpiCode", filter.KpiCodes, args);
        if (includeAnomalyStatus)
        {
            where += In("a.status", "anomalyStatus", filter.Statuses, args);
            where += In("a.anomaly_type", "anomalyType", filter.Types, args);
        }

        return where;
    }

    private List<Dictionary<string, object?>> CellRows(DbConnection connection, Filter filter, string where, Dictionary<string, object?> args)
    {
        var aggregateSql = KpiAggregateSql + " FROM kpi_cell_hourly k WHERE k.ts_hour>=@from AND k.ts_hour<@to GROUP BY k.cell_id";
        var anomalyWhere = AnomalyWhere(filter, args, includeAnomalyStatus: false);
        var sql = "SELECT c.cell_id AS cellId,c.bts_id AS btsId,c.local_cell_id AS localCellId,c.sector,ne.code AS btsCode," + "COALESCE(ne.full_name,ne.short_name,ne.code) AS btsName,ne.status,ne.region_id AS regionId," + "(SELECT COUNT(*) FROM anomalies a WHERE a.cell_id=c.cell_id AND " + anomalyWhere + ") AS anomalyCount," + "(SELECT CASE MAX(CASE a.severity WHEN 'critical' THEN 3 WHEN 'warning' THEN 2 ELSE 1 END) WHEN 3 THEN 'critical' WHEN 2 THEN 'warning' WHEN 1 THEN 'info' ELSE NULL END FROM anomalies a WHERE a.cell_id=c.cell_id AND a.status IN ('new','acknowledged') AND " + anomalyWhere + ") AS anomalySeverity," + "ka.observedSamples,ka.connAttempt,ka.connSuccess,ka.connDrop,ka.trafficDlBytes,ka.trafficUlBytes,ka.activeSamples" + CellJoin + "LEFT JOIN (" + aggregateSql + ") ka ON ka.cell_id=c.cell_id WHERE " + where + " ORDER BY c.cell_id LIMIT @limit OFFSET @offset";
        var rows = Rows(connection, sql, args);
        foreach (var row in rows)
        {
            row["kpis"] = KpiResult(row, filter, 1);
            foreach (var field in new[]
            {
                "observedSamples",
                "connAttempt",
                "connSuccess",
                "connDrop",
                "trafficDlBytes",
                "trafficUlBytes",
                "activeSamples"
            }

            )
                row.Remove(field);
        }

        return rows;
    }

    private const string KpiAggregateSql = "SELECT k.cell_id,COUNT(*) AS observedSamples,SUM(CAST(k.conn_attempt AS DECIMAL(30,0))) AS connAttempt," + "SUM(CAST(k.conn_success AS DECIMAL(30,0))) AS connSuccess,SUM(CAST(k.conn_drop AS DECIMAL(30,0))) AS connDrop," + "SUM(CAST(k.traffic_dl_bytes AS DECIMAL(30,0))) AS trafficDlBytes,SUM(CAST(k.traffic_ul_bytes AS DECIMAL(30,0))) AS trafficUlBytes," + "SUM(CASE WHEN k.conn_attempt>0 OR k.traffic_dl_bytes>0 OR k.traffic_ul_bytes>0 THEN 1 ELSE 0 END) AS activeSamples";
    private static Dictionary<string, object?> Aggregate(DbConnection connection, Filter filter, string where, Dictionary<string, object?> args, long cells)
    {
        // The single aggregate sees one KPI row per cell/hour; no anomaly/incident joins can multiply counters.
        var sql = KpiAggregateSql.Replace("SELECT k.cell_id,", "SELECT ") + " FROM kpi_cell_hourly k JOIN cells c ON c.cell_id=k.cell_id JOIN network_elements ne ON ne.id=c.bts_id WHERE k.ts_hour>=@from AND k.ts_hour<@to AND " + where;
        var row = Rows(connection, sql, args).Single();
        return KpiResult(row, filter, cells);
    }

    private static Dictionary<string, object?> KpiResult(Dictionary<string, object?> row, Filter filter, long cellCount)
    {
        var observed = Number(row, "observedSamples");
        var expected = checked(cellCount * ExpectedHours(filter));
        var attempts = Decimal(row, "connAttempt");
        var successes = Decimal(row, "connSuccess");
        var drops = Decimal(row, "connDrop");
        var dl = Decimal(row, "trafficDlBytes");
        var ul = Decimal(row, "trafficUlBytes");
        return new Dictionary<string, object?>
        {
            ["connAttempt"] = observed == 0 ? null : attempts,
            ["connSuccess"] = observed == 0 ? null : successes,
            ["connDrop"] = observed == 0 ? null : drops,
            ["trafficDlBytes"] = observed == 0 ? null : dl,
            ["trafficUlBytes"] = observed == 0 ? null : ul,
            ["cssrPct"] = observed == 0 ? null : Ratio(successes, attempts),
            ["dcrPct"] = observed == 0 ? null : Ratio(drops, successes),
            ["trafficDlGb"] = observed == 0 ? null : dl / 1000000000m,
            ["trafficUlGb"] = observed == 0 ? null : ul / 1000000000m,
            ["trafficTotalGb"] = observed == 0 ? null : (dl + ul) / 1000000000m,
            ["availabilityPct"] = null,
            ["availabilityBasis"] = "traffic_activity_proxy",
            ["trafficActivityPct"] = observed == 0 ? null : Ratio(Decimal(row, "activeSamples"), observed),
            ["coverage"] = new
            {
                observedSamples = observed,
                expectedSamples = expected,
                missingSamples = Math.Max(0, expected - observed),
                coveragePct = Ratio(observed, expected),
                cellCount,
                expectedHoursPerCell = ExpectedHours(filter),
                basis = "hourly timestamps in [from,to); current cell inventory"
            }
        };
    }

    private const string AnomalySelect = "SELECT a.anomaly_id AS anomalyId,a.detected_at AS detectedAt,a.cell_id AS cellId,c.bts_id AS btsId," + "c.sector,ne.code AS btsCode,COALESCE(ne.full_name,ne.short_name,ne.code) AS btsName,ne.region_id AS regionId," + "a.ts_from AS tsFrom,a.ts_to AS tsTo,a.anomaly_type AS type,a.kpi_code AS kpiCode,a.severity,a.status," + "a.observed_value AS observedValue,a.expected_value AS expectedValue,a.score,a.explained_by_incident AS explainedByIncident" + AnomalyJoin;
    private const string IncidentSelect = "SELECT i.incident_id AS incidentId,i.network_element_id AS networkElementId,i.begin_time AS beginTime,i.end_time AS endTime,i.severity,i.category,i.descr FROM incidents i";
    private static void AddIncidentLinks(List<Dictionary<string, object?>> incidents)
    {
        foreach (var incident in incidents)
            incident["href"] = "/api/incidents/" + Convert.ToString(incident["incidentId"], CultureInfo.InvariantCulture);
    }

    private IActionResult Read(Func<DbConnection, IActionResult> read)
    {
        var connection = _db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        try
        {
            return read(connection);
        }
        catch (MonitoringValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        finally
        {
            if (opened && connection.State != ConnectionState.Closed)
                connection.Close();
        }
    }

    private static List<Dictionary<string, object?>> Rows(DbConnection connection, string sql, Dictionary<string, object?> args)
    {
        using var command = Command(connection, sql, args);
        using var reader = command.ExecuteReader();
        var result = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                object? value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                if (value is DateTime timestamp)
                    value = new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
                row[reader.GetName(index)] = value;
            }

            result.Add(row);
        }

        return result;
    }

    private static long Scalar(DbConnection connection, string sql, Dictionary<string, object?> args)
    {
        using var command = Command(connection, sql, args);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static DbCommand Command(DbConnection connection, string sql, Dictionary<string, object?> args)
    {
        // Open after validation so malformed requests receive 400 even when the database is unavailable.
        if (connection.State != ConnectionState.Open)
            connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        foreach (var pair in args)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = pair.Key;
            parameter.Value = pair.Value ?? DBNull.Value;
            if (pair.Value is DateTime)
                parameter.DbType = DbType.DateTime;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static Filter Parse(MonitoringQuery query, bool anomalyFilters = false)
    {
        if (query.Page < 1)
            throw Invalid("page must be at least 1.");
        if (query.PageSize is < 1 or > 200)
            throw Invalid("pageSize must be between 1 and 200.");
        if (query.RegionId is 0 or > ushort.MaxValue)
            throw Invalid("regionId must be a positive SMALLINT identifier.");
        if (query.BtsId == 0 || query.CellId == 0)
            throw Invalid("btsId and cellId must be positive identifiers.");
        if (query.Search?.Length > 200)
            throw Invalid("search must be at most 200 characters.");
        var period = ParsePeriod(query.From, query.To);
        return new Filter
        {
            From = period.From,
            To = period.To,
            Page = query.Page,
            PageSize = query.PageSize,
            RegionId = query.RegionId,
            BtsId = query.BtsId,
            CellId = query.CellId,
            Statuses = Values(query.Status, anomalyFilters ? AnomalyStatuses : ElementStatuses, "status"),
            Types = Values(query.Type, anomalyFilters ? AnomalyTypes : ElementTypes, "type"),
            Severities = Values(query.Severity, Severities, "severity"),
            KpiCodes = Values(query.KpiCode, KpiCodes, "kpiCode"),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };
    }

    private static (DateTimeOffset From, DateTimeOffset To) ParsePeriod(string? from, string? to, bool required = false)
    {
        if ((from == null) != (to == null))
            throw Invalid("from and to must be provided together.");
        if (from == null)
        {
            if (required)
                throw Invalid("from and to are required.");
            var now = DateTimeOffset.UtcNow;
            return (now.AddHours(-24), now);
        }

        var start = OffsetDate(from, "from");
        var end = OffsetDate(to!, "to");
        if (start < DateTimeOffset.UnixEpoch)
            throw Invalid("from must be on or after 1970-01-01T00:00:00Z.");
        if (end <= start)
            throw Invalid("from must be earlier than to.");
        if (end > DateTimeOffset.UtcNow.AddMinutes(1))
            throw Invalid("to cannot be in the future (one minute clock tolerance).");
        if ((end - start).TotalDays > 366)
            throw Invalid("The maximum period is 366 days.");
        return (start, end);
    }

    private static DateTimeOffset OffsetDate(string text, string name)
    {
        if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            throw Invalid(name + " must be an ISO 8601 timestamp with seconds and an explicit Z or numeric offset.");
        return result.ToUniversalTime();
    }

    private static long ExpectedHours(Filter filter)
    {
        // Count whole UTC hour timestamps that satisfy the same SQL interval predicate.
        var first = (long)Math.Ceiling((filter.From - DateTimeOffset.UnixEpoch).TotalHours);
        var end = (long)Math.Ceiling((filter.To - DateTimeOffset.UnixEpoch).TotalHours);
        return Math.Max(0, end - first);
    }

    private static Dictionary<string, object?> Parameters(Filter filter) => new()
    {
        ["@from"] = filter.From.UtcDateTime,
        ["@to"] = filter.To.UtcDateTime,
        ["@regionId"] = filter.RegionId,
        ["@btsId"] = filter.BtsId,
        ["@cellId"] = filter.CellId
    };
    private static string[] Values(string? text, string[] allowed, string name)
    {
        if (text == null)
            return Array.Empty<string>();
        var values = text.Split(',').Select(value => value.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (values.Length == 0 || values.Any(value => !allowed.Contains(value, StringComparer.Ordinal)))
            throw Invalid(name + " must contain only: " + string.Join(", ", allowed) + ".");
        return values;
    }

    private static string In(string column, string name, string[] values, Dictionary<string, object?> args)
    {
        if (values.Length == 0)
            return "";
        var names = new List<string>();
        for (var index = 0; index < values.Length; index++)
        {
            var parameter = "@" + name + index;
            args[parameter] = values[index];
            names.Add(parameter);
        }

        return " AND " + column + " IN (" + string.Join(",", names) + ")";
    }

    private static string EscapeLike(string value) => value.Replace("=", "==").Replace("%", "=%").Replace("_", "=_");
    private static long Number(Dictionary<string, object?> row, string name) => row.TryGetValue(name, out var value) && value != null ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;
    private static decimal Decimal(Dictionary<string, object?> row, string name) => row.TryGetValue(name, out var value) && value != null ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0m;
    private static decimal? Ratio(decimal numerator, decimal denominator) => denominator == 0 ? null : Math.Round(100m * numerator / denominator, 4);
    private static void ValidateId(long id)
    {
        if (id <= 0 || id > uint.MaxValue)
            throw Invalid("id must be a positive unsigned 32-bit identifier.");
    }

    private static MonitoringValidationException Invalid(string message) => new(message);
    private sealed class MonitoringValidationException(string message) : Exception(message);
    private sealed class Filter
    {
        public DateTimeOffset From { get; init; }
        public DateTimeOffset To { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
        public uint? RegionId { get; init; }
        public uint? BtsId { get; init; }
        public uint? CellId { get; init; }
        public string[] Statuses { get; init; } = Array.Empty<string>();
        public string[] Types { get; init; } = Array.Empty<string>();
        public string[] Severities { get; init; } = Array.Empty<string>();
        public string[] KpiCodes { get; init; } = Array.Empty<string>();
        public string? Search { get; init; }
    }
}

// DTOs intentionally stay in this controller file, alongside the API that owns them.
public sealed class MonitoringQuery
{
    public string? From { get; set; }
    public string? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public uint? RegionId { get; set; }
    public uint? BtsId { get; set; }
    public uint? CellId { get; set; }
    public string? Status { get; set; }
    public string? Severity { get; set; }
    public string? Type { get; set; }
    public string? KpiCode { get; set; }
    public string? Search { get; set; }
    public bool ForMap { get; set; }
}

public sealed class MonitoringDetectRequest
{
    public int HoursBack { get; set; } = 720;
}

public sealed class MonitoringPeriodRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}
