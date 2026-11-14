using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using MySqlConnector;

namespace MotivCellScopeApiServices.Controllers;
[ApiController]
[Route("api/incidents")]
[Route("api/accidents")]
[Authorize(Roles = "engineer,operator,admin")]
[IncidentsController.ValidationErrors]
public sealed class IncidentsController : ControllerBase
{
    private readonly MotivReportDbContext _db;
    public IncidentsController(MotivReportDbContext db) => _db = db;
    [HttpGet]
    public IActionResult Get([FromQuery] IncidentQuery query) => Handle(() =>
    {
        var filter = ValidateQuery(query);
        using var connection = OpenConnection(_db);
        return Ok(ReadPage(connection, filter));
    });
    [HttpGet("{id}")]
    public IActionResult GetById(string id) => Handle(() =>
    {
        var key = ParseId(id);
        using var connection = OpenConnection(_db);
        var incident = ReadOne(connection, key);
        return incident is null ? Missing() : Ok(incident);
    });
    [HttpGet("export")]
    public IActionResult Export([FromQuery] IncidentQuery query) => Handle(() =>
    {
        var filter = ValidateQuery(query);
        using var connection = OpenConnection(_db);
        return File(IncidentCsv(ReadRows(connection, filter)), "text/csv; charset=utf-8", "incidents.csv");
    });
    [HttpGet("{id}/anomalies")]
    public IActionResult Anomalies(string id) => Handle(() =>
    {
        var key = ParseId(id);
        using var connection = OpenConnection(_db);
        using (var exists = Command(connection, "SELECT 1 FROM incidents WHERE incident_id=@id", null, ("@id", key)))
            if (exists.ExecuteScalar()is null)
                return Missing();
        using var command = Command(connection, """
            SELECT anomaly_id,detected_at,cell_id,ts_from,ts_to,anomaly_type,kpi_code,severity,
                   observed_value,expected_value,score,status,explained_by_incident
            FROM anomalies WHERE explained_by_incident=@id ORDER BY ts_from DESC,anomaly_id DESC
            """, null, ("@id", key));
        using var reader = command.ExecuteReader();
        var rows = new List<AnomalyDto>();
        while (reader.Read())
            rows.Add(new AnomalyDto { AnomalyId = reader.GetUInt64(0), DetectedAt = Utc(reader.GetDateTime(1)), CellId = NullableUInt32(reader, 2), TsFrom = Utc(reader.GetDateTime(3)), TsTo = Utc(reader.GetDateTime(4)), AnomalyType = reader.GetString(5), KpiCode = NullableString(reader, 6), Severity = reader.GetString(7), ObservedValue = NullableDecimal(reader, 8), ExpectedValue = NullableDecimal(reader, 9), Score = NullableDecimal(reader, 10), Status = reader.GetString(11), ExplainedByIncident = reader.GetUInt64(12) });
        return Ok(rows);
    });
    [HttpPost]
    [Authorize(Roles = "operator,admin")]
    public IActionResult Create([FromBody] IncidentInput? input) => Handle(() =>
    {
        var value = ValidateInput(input);
        using var connection = OpenConnection(_db);
        using var transaction = connection.BeginTransaction();
        var actor = ReadActor(connection, transaction);
        ValidateReferences(connection, transaction, value);
        using (var command = Command(connection, """
            INSERT INTO incidents
              (pw,direction_id,tech_center_id,priority_id,network_type_id,serv_elem,numb_rbs,equipment_type_id,
               alarm_type_id,descr,begin_time,end_time,akb,dgu,lenght,prostoy,common_prostoy,alarm_who,desc_notice,
               admin_district,network_element_id,region_id,severity,category,created_by_user_id,created_at,updated_at)
            VALUES (@pw,@directionId,@techCenterId,@priorityId,@networkTypeId,@servElem,@numbRbs,@equipmentTypeId,
               @alarmTypeId,@descr,@beginTime,@endTime,@akb,@dgu,@lenght,@prostoy,@commonProstoy,@alarmWho,@descNotice,
               @adminDistrict,@networkElementId,@regionId,@severity,@category,@actor,UTC_TIMESTAMP(3),UTC_TIMESTAMP(3))
            """, transaction, WriteParameters(value, actor.Id)))
            command.ExecuteNonQuery();
        ulong id;
        using (var command = Command(connection, "SELECT LAST_INSERT_ID()", transaction))
            id = Convert.ToUInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        Audit(connection, transaction, actor, "create_incident", id);
        var result = ReadOne(connection, id, transaction)!;
        transaction.Commit();
        return Created($"/api/incidents/{id.ToString(CultureInfo.InvariantCulture)}", result);
    });
    [HttpPut("{id}")]
    [Authorize(Roles = "operator,admin")]
    public IActionResult Update(string id, [FromBody] IncidentInput? input) => Handle(() =>
    {
        var key = ParseId(id);
        var value = ValidateInput(input);
        using var connection = OpenConnection(_db);
        using var transaction = connection.BeginTransaction();
        var actor = ReadActor(connection, transaction);
        if (!LockIncident(connection, transaction, key, input!.ExpectedUpdatedAt))
            return Missing();
        ValidateReferences(connection, transaction, value);
        var parameters = WriteParameters(value, actor.Id).Append(("@id", (object? )key)).ToArray();
        using (var command = Command(connection, """
            UPDATE incidents SET pw=@pw,direction_id=@directionId,tech_center_id=@techCenterId,priority_id=@priorityId,
              network_type_id=@networkTypeId,serv_elem=@servElem,numb_rbs=@numbRbs,equipment_type_id=@equipmentTypeId,
              alarm_type_id=@alarmTypeId,descr=@descr,begin_time=@beginTime,end_time=@endTime,akb=@akb,dgu=@dgu,
              lenght=@lenght,prostoy=@prostoy,common_prostoy=@commonProstoy,alarm_who=@alarmWho,desc_notice=@descNotice,
              admin_district=@adminDistrict,network_element_id=@networkElementId,region_id=@regionId,
              severity=@severity,category=@category,
              updated_at=GREATEST(UTC_TIMESTAMP(3),TIMESTAMPADD(MICROSECOND,1000,updated_at))
            WHERE incident_id=@id
            """, transaction, parameters))
            command.ExecuteNonQuery();
        Audit(connection, transaction, actor, "update_incident", key);
        var result = ReadOne(connection, key, transaction)!;
        transaction.Commit();
        return Ok(result);
    });
    [HttpDelete("{id}")]
    [Authorize(Roles = "operator,admin")]
    public IActionResult Delete(string id, [FromQuery] string? expectedUpdatedAt = null) => Handle(() =>
    {
        var key = ParseId(id);
        using var connection = OpenConnection(_db);
        using var transaction = connection.BeginTransaction();
        var actor = ReadActor(connection, transaction);
        if (!LockIncident(connection, transaction, key, expectedUpdatedAt))
            return Missing();
        using (var unlink = Command(connection, "UPDATE anomalies SET explained_by_incident=NULL WHERE explained_by_incident=@id", transaction, ("@id", key)))
            unlink.ExecuteNonQuery();
        using (var command = Command(connection, "DELETE FROM incidents WHERE incident_id=@id", transaction, ("@id", key)))
            command.ExecuteNonQuery();
        Audit(connection, transaction, actor, "delete_incident", key);
        transaction.Commit();
        return NoContent();
    });
    internal static IActionResult Handle(Func<IActionResult> action)
    {
        try
        {
            return action();
        }
        catch (ApiError error)
        {
            return new ObjectResult(new ErrorDto(error.Code, error.Message, error.Field))
            {
                StatusCode = error.Status
            };
        }
        catch (MySqlException error)when (error.Number == 1452)
        {
            return new BadRequestObjectResult(new ErrorDto("invalid_reference", "A referenced record does not exist."));
        }
        catch (MySqlException error)when (error.Number is 1062 or 1451 or 1213 or 1205)
        {
            return new ConflictObjectResult(new ErrorDto("conflict", "The operation conflicts with another record or concurrent change. Retry after reloading."));
        }
        catch (MySqlException)
        {
            return new ObjectResult(new ErrorDto("database_error", "The database operation could not be completed."))
            {
                StatusCode = 500
            };
        }
    }

    internal static MySqlConnection OpenConnection(MotivReportDbContext db)
    {
        var connectionString = db.Database.GetConnectionString() ?? throw new InvalidOperationException("The report database connection string is not configured.");
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            DateTimeKind = MySqlDateTimeKind.Utc
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            using var command = Command(connection, "SET time_zone = '+00:00'");
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static MySqlCommand Command(MySqlConnection connection, string sql, MySqlTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    internal static ValidatedQuery ValidateQuery(IncidentQuery query)
    {
        var now = DateTime.UtcNow;
        DateTime from, to;
        if (query.From is null && query.To is null)
        {
            from = now.AddDays(-30);
            to = now;
        }
        else
        {
            if (query.From is null || query.To is null)
                throw Invalid("from", "from and to must be supplied together.");
            from = ParseInstant(query.From, "from");
            to = ParseInstant(query.To, "to");
            if (from >= to)
                throw Invalid("to", "to must be later than from; the interval is [from, to).");
        }

        if (query.Page < 1)
            throw Invalid("page", "page must be at least 1.");
        if (query.PageSize is < 1 or > 200)
            throw Invalid("pageSize", "pageSize must be between 1 and 200.");
        ValidateEnum(query.Category, Categories, "category", true);
        ValidateEnum(query.Severity, Severities, "severity", true);
        ValidateEnum(query.Status, ["all", "open", "closed"], "status");
        ValidateEnum(query.Sort, ["begin_time", "severity", "pw"], "sort");
        ValidateEnum(query.Order, ["asc", "desc"], "order");
        foreach (var pair in new (string Field, ushort? Value)[]
        {
            ("directionId", query.DirectionId),
            ("techCenterId", query.TechCenterId),
            ("priorityId", query.PriorityId),
            ("networkTypeId", query.NetworkTypeId),
            ("equipmentTypeId", query.EquipmentTypeId),
            ("alarmTypeId", query.AlarmTypeId),
            ("regionId", query.RegionId)
        }

        )
            if (pair.Value == 0)
                throw Invalid(pair.Field, "ID must be greater than zero.");
        CheckLength(query.Search, 200, "search");
        var clauses = new List<string>
        {
            "i.begin_time < @to",
            "(i.end_time IS NULL OR i.end_time > @from)"
        };
        var parameters = new List<(string, object? )>
        {
            ("@from", from),
            ("@to", to)
        };
        void Add(string column, string name, object? value)
        {
            if (value is not null)
            {
                clauses.Add(column + " = @" + name);
                parameters.Add(("@" + name, value));
            }
        }

        Add("i.direction_id", "directionId", query.DirectionId);
        Add("i.tech_center_id", "techCenterId", query.TechCenterId);
        Add("i.priority_id", "priorityId", query.PriorityId);
        Add("i.network_type_id", "networkTypeId", query.NetworkTypeId);
        Add("i.equipment_type_id", "equipmentTypeId", query.EquipmentTypeId);
        Add("i.alarm_type_id", "alarmTypeId", query.AlarmTypeId);
        Add("i.region_id", "regionId", query.RegionId);
        Add("i.category", "category", query.Category);
        Add("i.severity", "severity", query.Severity);
        if (query.Status == "open")
            clauses.Add("i.end_time IS NULL");
        if (query.Status == "closed")
            clauses.Add("i.end_time IS NOT NULL");
        if (!string.IsNullOrEmpty(query.Search))
        {
            clauses.Add("(" + string.Join(" OR ", new[] { "i.pw", "i.serv_elem", "i.descr", "i.desc_notice", "i.alarm_who", "i.admin_district", "d.name", "tc.name", "nt.name", "atp.name", "r.name" }.Select(c => c + " LIKE @search ESCAPE '='")) + ")");
            parameters.Add(("@search", "%" + query.Search.Replace("=", "==").Replace("%", "=%").Replace("_", "=_") + "%"));
        }

        return new ValidatedQuery(query, from, to, " WHERE " + string.Join(" AND ", clauses), parameters.ToArray());
    }

    internal static PageDto ReadPage(MySqlConnection connection, ValidatedQuery filter)
    {
        long total;
        using (var command = Command(connection, "SELECT COUNT(*) " + Joins + filter.Where, null, filter.Parameters))
            total = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return new PageDto(ReadRows(connection, filter, true), total, filter.Query.Page, filter.Query.PageSize, filter.From, filter.To);
    }

    internal static List<IncidentDto> ReadRows(MySqlConnection connection, ValidatedQuery filter, bool paginated = false)
    {
        var sql = Select + Joins + filter.Where + " ORDER BY i." + filter.Query.Sort + " " + filter.Query.Order + ",i.incident_id " + filter.Query.Order;
        var parameters = filter.Parameters.ToList();
        if (paginated)
        {
            sql += " LIMIT @limit OFFSET @offset";
            parameters.Add(("@limit", filter.Query.PageSize));
            parameters.Add(("@offset", ((long)filter.Query.Page - 1) * filter.Query.PageSize));
        }

        using var command = Command(connection, sql, null, parameters.ToArray());
        using var reader = command.ExecuteReader();
        var rows = new List<IncidentDto>();
        var now = DateTime.UtcNow;
        while (reader.Read())
            rows.Add(ReadIncident(reader, now));
        return rows;
    }

    private static IncidentDto? ReadOne(MySqlConnection connection, ulong id, MySqlTransaction? transaction = null)
    {
        using var command = Command(connection, Select + Joins + " WHERE i.incident_id=@id", transaction, ("@id", id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadIncident(reader, DateTime.UtcNow) : null;
    }

    private const string Select = """
        SELECT i.*,d.code direction_code,d.name direction_name,tc.code tech_center_code,tc.name tech_center_name,
          p.code priority_code,p.name priority_name,nt.code network_type_code,nt.name network_type_name,
          et.code equipment_type_code,et.name equipment_type_name,atp.code alarm_type_code,atp.name alarm_type_name,
          ne.full_name network_element_name,r.code region_code,r.name region_name,u.username created_by_username
        """;
    private const string Joins = """
         FROM incidents i
         LEFT JOIN directions d ON d.direction_id=i.direction_id
         LEFT JOIN technical_centers tc ON tc.tech_center_id=i.tech_center_id
         LEFT JOIN incident_priorities p ON p.priority_id=i.priority_id
         LEFT JOIN network_types nt ON nt.network_type_id=i.network_type_id
         LEFT JOIN equipment_types et ON et.equipment_type_id=i.equipment_type_id
         LEFT JOIN alarm_types atp ON atp.alarm_type_id=i.alarm_type_id
         LEFT JOIN network_elements ne ON ne.id=i.network_element_id
         LEFT JOIN regions r ON r.region_id=i.region_id
         LEFT JOIN users u ON u.user_id=i.created_by_user_id
        """;
    private static IncidentDto ReadIncident(MySqlDataReader reader, DateTime now)
    {
        string? S(string name) => NullableString(reader, reader.GetOrdinal(name));
        ushort? Id(string name)
        {
            var n = reader.GetOrdinal(name);
            return reader.IsDBNull(n) ? null : reader.GetUInt16(n);
        }

        uint? Id32(string name) => NullableUInt32(reader, reader.GetOrdinal(name));
        string? Time(string name)
        {
            var n = reader.GetOrdinal(name);
            return reader.IsDBNull(n) ? null : FormatSeconds((long)reader.GetTimeSpan(n).TotalSeconds);
        }

        var begin = Utc(reader.GetDateTime("begin_time"));
        DateTime? end = reader.IsDBNull(reader.GetOrdinal("end_time")) ? null : Utc(reader.GetDateTime("end_time"));
        var duration = Math.Max(0, (long)((end ?? now) - begin).TotalSeconds);
        var prostoy = Time("prostoy");
        var common = CommonSeconds(prostoy, S("numb_rbs"));
        return new IncidentDto
        {
            IncidentId = reader.GetUInt64("incident_id"),
            Pw = S("pw"),
            DirectionId = Id("direction_id"),
            DirectionCode = S("direction_code"),
            DirectionName = S("direction_name"),
            TechCenterId = Id("tech_center_id"),
            TechCenterCode = S("tech_center_code"),
            TechCenterName = S("tech_center_name"),
            PriorityId = Id("priority_id"),
            PriorityCode = S("priority_code"),
            PriorityName = S("priority_name"),
            NetworkTypeId = Id("network_type_id"),
            NetworkTypeCode = S("network_type_code"),
            NetworkTypeName = S("network_type_name"),
            EquipmentTypeId = Id("equipment_type_id"),
            EquipmentTypeCode = S("equipment_type_code"),
            EquipmentTypeName = S("equipment_type_name"),
            AlarmTypeId = Id("alarm_type_id"),
            AlarmTypeCode = S("alarm_type_code"),
            AlarmTypeName = S("alarm_type_name"),
            ServElem = S("serv_elem"),
            NumbRbs = S("numb_rbs"),
            Descr = S("descr"),
            BeginTime = begin,
            EndTime = end,
            Akb = Time("akb"),
            Dgu = Time("dgu"),
            Lenght = FormatSeconds(duration),
            Prostoy = prostoy,
            CommonProstoy = common.HasValue ? FormatSeconds(common.Value) : null,
            AlarmWho = S("alarm_who"),
            DescNotice = S("desc_notice"),
            AdminDistrict = S("admin_district"),
            NetworkElementId = Id32("network_element_id"),
            NetworkElementName = S("network_element_name"),
            RegionId = Id("region_id"),
            RegionCode = S("region_code"),
            RegionName = S("region_name"),
            Severity = reader.GetString("severity"),
            Category = reader.GetString("category"),
            Status = end is null ? "open" : "closed",
            CreatedByUserId = Id32("created_by_user_id"),
            CreatedByUsername = S("created_by_username"),
            CreatedAt = Utc(reader.GetDateTime("created_at")),
            UpdatedAt = Utc(reader.GetDateTime("updated_at")),
            DurationSeconds = duration,
            CommonProstoySeconds = common
        };
    }

    private static ValidatedInput ValidateInput(IncidentInput? input)
    {
        if (input is null)
            throw Invalid("body", "An incident body is required.");
        var begin = WholeSecond(ParseInstant(input.BeginTime, "beginTime"));
        DateTime? end = input.EndTime is null ? null : WholeSecond(ParseInstant(input.EndTime, "endTime"));
        if (end < begin)
            throw Invalid("endTime", "endTime cannot be earlier than beginTime.");
        var now = DateTime.UtcNow;
        if (begin > now)
            throw Invalid("beginTime", "beginTime cannot be in the future.");
        if (end > now)
            throw Invalid("endTime", "endTime cannot be in the future.");
        CheckLength(input.Pw, 450, "pw");
        CheckLength(input.ServElem, 450, "servElem");
        CheckLength(input.Descr, 450, "descr");
        CheckLength(input.AlarmWho, 450, "alarmWho");
        CheckLength(input.DescNotice, 16000, "descNotice");
        CheckLength(input.AdminDistrict, 128, "adminDistrict");
        ValidateEnum(input.Severity, Severities, "severity");
        ValidateEnum(input.Category, Categories, "category");
        foreach (var pair in new (string Field, ulong? Id)[]
        {
            ("directionId", input.DirectionId),
            ("techCenterId", input.TechCenterId),
            ("priorityId", input.PriorityId),
            ("networkTypeId", input.NetworkTypeId),
            ("equipmentTypeId", input.EquipmentTypeId),
            ("alarmTypeId", input.AlarmTypeId),
            ("regionId", input.RegionId),
            ("networkElementId", input.NetworkElementId)
        }

        )
            if (pair.Id == 0)
                throw Invalid(pair.Field, "ID must be greater than zero.");
        var akb = ParseDuration(input.Akb, "akb");
        var dgu = ParseDuration(input.Dgu, "dgu");
        var prostoy = ParseDuration(input.Prostoy, "prostoy");
        var count = input.NumbRbs?.Trim();
        if (count is not null)
        {
            if (count is "C" or "С" or "c" or "с")
                count = "С";
            else if (Regex.IsMatch(count, "^[0-9]+$") && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 10000)
                count = n.ToString(CultureInfo.InvariantCulture);
            else
                throw Invalid("numbRbs", "numbRbs must be null, a count from 1 to 10000, or С/C for sectors.");
        }

        if (input.ExpectedUpdatedAt is not null)
            ParseInstant(input.ExpectedUpdatedAt, "expectedUpdatedAt");
        return new ValidatedInput(input, begin, end, akb, dgu, prostoy, count, input.ServElem);
    }

    private static void ValidateReferences(MySqlConnection connection, MySqlTransaction transaction, ValidatedInput value)
    {
        var input = value.Input;
        void Require(string table, string column, object? id, string field)
        {
            if (id is null)
                return;
            using var command = Command(connection, $"SELECT 1 FROM {table} WHERE {column}=@id", transaction, ("@id", id));
            if (command.ExecuteScalar()is null)
                throw Invalid(field, "The referenced record does not exist.");
        }

        Require("directions", "direction_id", input.DirectionId, "directionId");
        Require("incident_priorities", "priority_id", input.PriorityId, "priorityId");
        Require("network_types", "network_type_id", input.NetworkTypeId, "networkTypeId");
        Require("equipment_types", "equipment_type_id", input.EquipmentTypeId, "equipmentTypeId");
        Require("alarm_types", "alarm_type_id", input.AlarmTypeId, "alarmTypeId");
        Require("regions", "region_id", input.RegionId, "regionId");
        ushort? DirectionOf(ushort? center, string field)
        {
            if (center is null)
                return null;
            using var command = Command(connection, "SELECT direction_id FROM technical_centers WHERE tech_center_id=@id", transaction, ("@id", center));
            var result = command.ExecuteScalar();
            if (result is null)
                throw Invalid(field, "The technical center does not exist.");
            return Convert.ToUInt16(result, CultureInfo.InvariantCulture);
        }

        var direction = DirectionOf(input.TechCenterId, "techCenterId");
        if (input.DirectionId.HasValue && direction.HasValue && input.DirectionId != direction)
            throw Invalid("techCenterId", "The technical center does not belong to the selected direction.");
        if (input.NetworkElementId is null)
            return;
        ushort? centerId, regionId;
        using (var command = Command(connection, "SELECT full_name,tech_center_id,region_id FROM network_elements WHERE id=@id", transaction, ("@id", input.NetworkElementId)))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
                throw Invalid("networkElementId", "The network element does not exist.");
            value.ServElem = NullableString(reader, 0);
            centerId = reader.IsDBNull(1) ? null : reader.GetUInt16(1);
            regionId = reader.IsDBNull(2) ? null : reader.GetUInt16(2);
        }

        if (input.TechCenterId.HasValue && centerId.HasValue && input.TechCenterId != centerId)
            throw Invalid("networkElementId", "The network element does not belong to the selected technical center.");
        if (input.RegionId.HasValue && regionId.HasValue && input.RegionId != regionId)
            throw Invalid("networkElementId", "The network element does not belong to the selected region.");
        var elementDirection = DirectionOf(centerId, "networkElementId");
        if (input.DirectionId.HasValue && elementDirection.HasValue && input.DirectionId != elementDirection)
            throw Invalid("networkElementId", "The network element does not belong to the selected direction.");
    }

    private Actor ReadActor(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (!uint.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new ApiError(401, "invalid_actor", "The authenticated user ID is invalid.");
        using var command = Command(connection, "SELECT username,role FROM users WHERE user_id=@id", transaction, ("@id", id));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new ApiError(401, "invalid_actor", "The authenticated user no longer exists.");
        return new Actor(id, reader.GetString(0), reader.GetString(1));
    }

    private void Audit(MySqlConnection connection, MySqlTransaction transaction, Actor actor, string action, ulong id)
    {
        using var command = Command(connection, """
            INSERT INTO audit_log (ts,user_id,username,role,action,entity_type,entity_id,ip_address,success)
            VALUES (UTC_TIMESTAMP(3),@user,@username,@role,@action,'incident',@id,@ip,1)
            """, transaction, ("@user", actor.Id), ("@username", actor.Username), ("@role", actor.Role), ("@action", action), ("@id", id), ("@ip", HttpContext.Connection.RemoteIpAddress?.ToString()));
        command.ExecuteNonQuery();
    }

    private static bool LockIncident(MySqlConnection connection, MySqlTransaction transaction, ulong id, string? expected)
    {
        var expectedTime = expected is null ? (DateTime? )null : ParseInstant(expected, "expectedUpdatedAt");
        using var command = Command(connection, "SELECT updated_at FROM incidents WHERE incident_id=@id FOR UPDATE", transaction, ("@id", id));
        var value = command.ExecuteScalar();
        if (value is null)
            return false;
        if (expectedTime.HasValue && Utc((DateTime)value) != expectedTime.Value)
            throw new ApiError(409, "concurrent_update", "The incident changed. Reload it and retry.", "expectedUpdatedAt");
        return true;
    }

    private static (string, object? )[] WriteParameters(ValidatedInput value, uint actor)
    {
        var input = value.Input;
        var duration = Math.Max(0, (long)((value.End ?? DateTime.UtcNow) - value.Begin).TotalSeconds);
        var common = CommonSeconds(value.Prostoy.HasValue ? FormatSeconds((long)value.Prostoy.Value.TotalSeconds) : null, value.NumbRbs);
        return[("@pw", input.Pw), ("@directionId", input.DirectionId), ("@techCenterId", input.TechCenterId), ("@priorityId", input.PriorityId), ("@networkTypeId", input.NetworkTypeId), ("@servElem", value.ServElem), ("@numbRbs", value.NumbRbs), ("@equipmentTypeId", input.EquipmentTypeId), ("@alarmTypeId", input.AlarmTypeId), ("@descr", input.Descr), ("@beginTime", value.Begin), ("@endTime", value.End), ("@akb", value.Akb), ("@dgu", value.Dgu), ("@lenght", FormatSeconds(duration)), ("@prostoy", value.Prostoy), ("@commonProstoy", common.HasValue ? FormatSeconds(common.Value) : null), ("@alarmWho", input.AlarmWho), ("@descNotice", input.DescNotice), ("@adminDistrict", input.AdminDistrict), ("@networkElementId", input.NetworkElementId), ("@regionId", input.RegionId), ("@severity", input.Severity), ("@category", input.Category), ("@actor", actor)];
    }

    internal static byte[] IncidentCsv(IEnumerable<IncidentDto> rows)
    {
        var csv = new StringBuilder();
        CsvRow(csv, ["pw", "direction", "techCenter", "priority", "networkType", "servElem", "numbRbs", "equipmentType", "alarmType", "descr", "beginTime", "endTime", "akb", "dgu", "lenght", "prostoy", "commonProstoy", "alarmWho", "descNotice", "adminDistrict", "region", "severity", "category"]);
        foreach (var i in rows)
            CsvRow(csv, [i.Pw, i.DirectionCode, i.TechCenterCode, i.PriorityCode, i.NetworkTypeCode, i.ServElem, i.NumbRbs, i.EquipmentTypeCode, i.AlarmTypeCode, i.Descr, Instant(i.BeginTime), i.EndTime.HasValue ? Instant(i.EndTime.Value) : null, i.Akb, i.Dgu, i.Lenght, i.Prostoy, i.CommonProstoy, i.AlarmWho, i.DescNotice, i.AdminDistrict, i.RegionName, i.Severity, i.Category]);
        return CsvBytes(csv);
    }

    internal static void CsvRow(StringBuilder csv, IEnumerable<string?> cells)
    {
        csv.AppendJoin(';', cells.Select(cell =>
        {
            var value = cell ?? "";
            var first = value.TrimStart();
            if (first.Length > 0 && (first[0] is '=' or '+' or '-' or '@' || value[0] is '\t' or '\r' or '\n'))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }));
        csv.Append("\r\n");
    }

    internal static byte[] CsvBytes(StringBuilder csv) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    internal static string Instant(DateTime value) => Utc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    internal static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime WholeSecond(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    internal static DateTime ParseInstant(string? value, string field)
    {
        if (value is null || !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.UtcDateTime.Year < 1000)
            throw Invalid(field, "Use an ISO 8601 timestamp with seconds and an explicit Z or ±HH:MM offset (year 1000 or later).");
        return parsed.UtcDateTime;
    }

    private static TimeSpan? ParseDuration(string? value, string field)
    {
        if (value is null)
            return null;
        var match = Regex.Match(value, @"^(\d{2,3}):([0-5]\d):([0-5]\d)$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var hours) || hours > 838)
            throw Invalid(field, "Use HH:MM:SS from 00:00:00 through 838:59:59.");
        return TimeSpan.FromSeconds(hours * 3600 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    private static long? CommonSeconds(string? prostoy, string? numbRbs)
    {
        if (prostoy is null || numbRbs is null || !Regex.IsMatch(numbRbs, "^[0-9]+$") || !int.TryParse(numbRbs, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 10000)
            return null;
        var parts = prostoy.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes) || !int.TryParse(parts[2], out var seconds) || hours < 0)
            return null;
        return (hours * 3600 + minutes * 60 + seconds) * count;
    }

    internal static string FormatSeconds(long value) => (value / 3600).ToString("D2", CultureInfo.InvariantCulture) + ":" + ((value / 60) % 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (value % 60).ToString("D2", CultureInfo.InvariantCulture);
    private static ulong ParseId(string value) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : throw Invalid("id", "ID must be a positive unsigned integer.");
    private static void CheckLength(string? value, int max, string field)
    {
        if (value?.Length > max)
            throw Invalid(field, $"Maximum length is {max} characters.");
    }

    private static void ValidateEnum(string? value, string[] allowed, string field, bool nullable = false)
    {
        if (!(nullable && value is null) && (value is null || !allowed.Contains(value, StringComparer.Ordinal)))
            throw Invalid(field, "Allowed values: " + string.Join(", ", allowed) + ".");
    }

    internal static ApiError Invalid(string field, string message) => new(400, "validation_error", message, field);
    private static IActionResult Missing() => new NotFoundObjectResult(new ErrorDto("not_found", "Incident not found."));
    internal static string? NullableString(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    internal static uint? NullableUInt32(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetUInt32(ordinal);
    private static decimal? NullableDecimal(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static readonly string[] Categories = ["power", "transport", "hardware", "software", "planned", "client", "external", "other"];
    private static readonly string[] Severities = ["low", "medium", "high", "critical"];
    public sealed class IncidentQuery
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
    }

    public sealed class IncidentInput
    {
        public string? Pw { get; set; }
        public ushort? DirectionId { get; set; }
        public ushort? TechCenterId { get; set; }
        public ushort? PriorityId { get; set; }
        public ushort? NetworkTypeId { get; set; }
        public string? ServElem { get; set; }
        public string? NumbRbs { get; set; }
        public ushort? EquipmentTypeId { get; set; }
        public ushort? AlarmTypeId { get; set; }
        public string? Descr { get; set; }
        public string? BeginTime { get; set; }
        public string? EndTime { get; set; }
        public string? Akb { get; set; }
        public string? Dgu { get; set; }
        public string? Prostoy { get; set; }
        public string? AlarmWho { get; set; }
        public string? DescNotice { get; set; }
        public string? AdminDistrict { get; set; }
        public uint? NetworkElementId { get; set; }
        public ushort? RegionId { get; set; }
        public string Severity { get; set; } = "medium";
        public string Category { get; set; } = "other";
        public string? ExpectedUpdatedAt { get; set; }
    }

    public sealed class IncidentDto
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
    }

    public sealed class AnomalyDto
    {
        public ulong AnomalyId { get; set; }
        public DateTime DetectedAt { get; set; }
        public uint? CellId { get; set; }
        public DateTime TsFrom { get; set; }
        public DateTime TsTo { get; set; }
        public string AnomalyType { get; set; } = "";
        public string? KpiCode { get; set; }
        public string Severity { get; set; } = "";
        public decimal? ObservedValue { get; set; }
        public decimal? ExpectedValue { get; set; }
        public decimal? Score { get; set; }
        public string Status { get; set; } = "";
        public ulong ExplainedByIncident { get; set; }
    }

    public sealed record PageDto(List<IncidentDto> Items, long Total, int Page, int PageSize, DateTime From, DateTime To);
    public sealed record ErrorDto(string Error, string Message, string? Field = null);
    internal sealed record ValidatedQuery(IncidentQuery Query, DateTime From, DateTime To, string Where, (string Name, object? Value)[] Parameters);
    private sealed record Actor(uint Id, string Username, string Role);
    private sealed record ValidatedInput(IncidentInput Input, DateTime Begin, DateTime? End, TimeSpan? Akb, TimeSpan? Dgu, TimeSpan? Prostoy, string? NumbRbs, string? InitialServElem)
    {
        public string? ServElem { get; set; } = InitialServElem;
    }

    internal sealed class ApiError(int status, string code, string message, string? field = null) : Exception(message)
    {
        public int Status { get; } = status;
        public string Code { get; } = code;
        public string? Field { get; } = field;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ValidationErrorsAttribute : ActionFilterAttribute
    {
        public ValidationErrorsAttribute() => Order = -3000;
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ModelState.IsValid)
                return;
            var entry = context.ModelState.FirstOrDefault(e => e.Value?.Errors.Count > 0);
            context.Result = new BadRequestObjectResult(new ErrorDto("validation_error", "The request contains a missing, invalid, or out-of-range value.", entry.Key));
        }
    }
}
