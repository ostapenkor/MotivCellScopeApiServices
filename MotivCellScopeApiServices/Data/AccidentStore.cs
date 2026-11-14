using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MotivCellScopeApiServices.Contracts;
using MySqlConnector;

namespace MotivCellScopeApiServices.Data;
// Простые параметризованные запросы. Каждая запись меняется одной транзакцией.
public partial class AccidentStore
{
    private readonly string connectionString;
    public AccidentStore(IConfiguration config) => connectionString = config.GetConnectionString("Journal")!;
    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        var db = new MySqlConnection(connectionString);
        try
        {
            await db.OpenAsync(ct);
            using var setup = new MySqlCommand("SET SESSION group_concat_max_len=65535", db);
            await setup.ExecuteNonQueryAsync(ct);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static void Parameters(MySqlCommand cmd, params (string Name, object? Value)[] values)
    {
        foreach (var p in values)
            cmd.Parameters.AddWithValue("@" + p.Name, p.Value ?? DBNull.Value);
    }

    private static object? Optional(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static decimal? Number(MySqlDataReader r, string name) => r.IsDBNull(r.GetOrdinal(name)) ? null : r.GetDecimal(name);
    private static string Local(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private const string Columns = "a.*,u.username AS author";
    private static AccidentDto Read(MySqlDataReader r, DateTimeOffset now)
    {
        var begin = r.GetDateTime("begin_at");
        DateTime? end = r.IsDBNull(r.GetOrdinal("end_at")) ? null : r.GetDateTime("end_at");
        bool local = r.GetString("time_basis") == "source_local";
        bool historical = !r.IsDBNull(r.GetOrdinal("source_row_id"));
        return new AccidentDto
        {
            Id = r.GetInt32("id"),
            Pw = r.GetString("PW"),
            Direction = r.GetString("NAPR"),
            Center = r.GetString("TEHC"),
            Category = r.IsDBNull(r.GetOrdinal("KATEG")) ? null : r.GetInt32("KATEG"),
            CategoryCode = r.GetString("category_code"),
            NeedsCategoryMapping = r.IsDBNull(r.GetOrdinal("KATEG")),
            NetworkType = r.GetString("NETW_TYPE"),
            ServiceElement = r.GetString("SERV_ELEM"),
            IsSector = r.GetBoolean("is_sector"),
            StationCount = r.IsDBNull(r.GetOrdinal("station_count")) ? null : r.GetInt32("station_count"),
            Equipment = r.GetString("EQIP_TYPE"),
            AlarmType = r.GetString("ALARM_TYPE"),
            Description = r.GetString("DESCR"),
            TimeBasis = local ? "source_local" : "utc",
            BeginTime = local ? null : Utc(begin),
            EndTime = local || end == null ? null : Utc(end.Value),
            BeginTimeLocal = local ? Local(begin) : null,
            EndTimeLocal = local && end != null ? Local(end.Value) : null,
            BatterySeconds = Number(r, "battery_seconds"),
            GeneratorSeconds = Number(r, "generator_seconds"),
            DowntimeSeconds = Number(r, "downtime_seconds"),
            Notified = r.GetString("notified"),
            Notes = r.GetString("notes"),
            Version = r.GetInt32("version"),
            CreatedBy = r.GetInt32("created_by"),
            CreatedByName = r.GetString("author"),
            DurationSeconds = Number(r, "duration_seconds"),
            TotalDowntimeSeconds = Number(r, "total_downtime_seconds"),
            ServerTimeUtc = now,
            Source = historical ? new
            {
                isHistorical = true,
                batchId = r.GetInt32("batch_id"),
                sheet = r.GetString("sheet_name"),
                row = r.GetInt32("row_number"),
                timeZone = r.IsDBNull(r.GetOrdinal("time_zone")) ? null : r.GetString("time_zone"),
                snapshotAtLocal = r.IsDBNull(r.GetOrdinal("snapshot_at")) ? null : Local(r.GetDateTime("snapshot_at"))
            }

            : null
        };
    }

    private const string Where = "a.begin_at < @to AND (a.end_at IS NULL OR a.end_at>@from) " + "AND (@direction IS NULL OR a.NAPR=@direction) AND (@center IS NULL OR a.TEHC=@center) " + "AND (@category IS NULL OR a.KATEG=@category) AND (@categoryCode IS NULL OR BINARY a.category_code=BINARY @categoryCode) " + "AND (@network IS NULL OR a.NETW_TYPE=@network OR EXISTS(SELECT 1 FROM journal_accident_networks nx JOIN journal_network_types nt ON nt.id=nx.network_type_id WHERE nx.accident_id=a.id AND BINARY nt.code=BINARY @network)) " + "AND (@scope='all' OR (@scope='historical' AND a.source_row_id IS NOT NULL) OR (@scope='live' AND a.source_row_id IS NULL)) " + "AND (@status='all' OR (@status='open' AND a.end_at IS NULL) OR (@status='closed' AND a.end_at IS NOT NULL)) " + "AND (@search IS NULL OR a.SERV_ELEM LIKE @search ESCAPE '=' OR a.DESCR LIKE @search ESCAPE '=' OR a.PW LIKE @search ESCAPE '=')";
    private static void FilterParameters(MySqlCommand cmd, AccidentFilter f)
    {
        string? search = string.IsNullOrWhiteSpace(f.Search) ? null : "%" + f.Search.Replace("=", "==").Replace("%", "=%").Replace("_", "=_") + "%";
        Parameters(cmd, ("from", f.UtcFrom), ("to", f.UtcTo), ("direction", Optional(f.Direction)), ("center", Optional(f.Center)), ("category", f.Category), ("categoryCode", Optional(f.CategoryCode)), ("network", Optional(f.NetworkType)), ("status", f.Status), ("scope", f.SourceScope), ("search", search));
    }

    public async Task<AccidentPage> List(AccidentFilter f, bool export, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        int total, open, categoryOne, unmapped, historicalOpen;
        using (var cmd = new MySqlCommand("SELECT COUNT(*),COALESCE(SUM(end_at IS NULL),0),COALESCE(SUM(KATEG=1),0),COALESCE(SUM(KATEG IS NULL),0),COALESCE(SUM(end_at IS NULL AND source_row_id IS NOT NULL),0) FROM journal_report a WHERE " + Where, db, tx))
        {
            FilterParameters(cmd, f);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            total = r.GetInt32(0);
            open = r.GetInt32(1);
            categoryOne = r.GetInt32(2);
            unmapped = r.GetInt32(3);
            historicalOpen = r.GetInt32(4);
        }

        var now = DateTimeOffset.UtcNow;
        var rows = new List<AccidentDto>();
        using (var cmd = new MySqlCommand("SELECT " + Columns + " FROM journal_report a JOIN users u ON u.user_id=a.created_by WHERE " + Where + " ORDER BY a.begin_at DESC,a.id DESC" + (export ? "" : " LIMIT @take OFFSET @skip"), db, tx))
        {
            FilterParameters(cmd, f);
            if (!export)
                Parameters(cmd, ("take", f.PageSize), ("skip", (f.Page - 1) * f.PageSize));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add(Read(r, now));
        }

        await tx.CommitAsync(ct);
        return new(total, f.Page, f.PageSize, now, new { total, open, closed = total - open, categoryOne, unmappedCategories = unmapped, historicalOpen, liveOpen = open - historicalOpen }, rows);
    }

    public async Task<AccidentDto?> Get(int id, CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var cmd = new MySqlCommand("SELECT " + Columns + " FROM journal_report a JOIN users u ON u.user_id=a.created_by WHERE a.id=@id", db);
        Parameters(cmd, ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r, DateTimeOffset.UtcNow) : null;
    }

    private static async Task Audit(MySqlConnection db, MySqlTransaction tx, int userId, int? id, string action, CancellationToken ct)
    {
        using var cmd = new MySqlCommand("INSERT INTO accident_audit(user_id,accident_id,action,occurred_at) VALUES(@user,@id,@action,UTC_TIMESTAMP())", db, tx);
        Parameters(cmd, ("user", userId), ("id", id), ("action", action));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Только эти имена таблиц используются в SQL. Пользовательские значения всегда в параметрах.
    private static string[] Networks(string value) => value is "LTE/GSM" or "GSM LTE" or "GSM, LTE" or "GSM/LTE" ? new[]
    {
        "GSM",
        "LTE"
    }

    : new[]
    {
        value
    };
    private static async Task<int> Code(MySqlConnection db, MySqlTransaction? tx, string table, string code, bool create, CancellationToken ct)
    {
        if (create)
        {
            using var add = new MySqlCommand($"INSERT IGNORE INTO {table}(code,label) VALUES(@code,@code)", db, tx);
            Parameters(add, ("code", code));
            await add.ExecuteNonQueryAsync(ct);
        }

        using var find = new MySqlCommand($"SELECT id FROM {table} WHERE BINARY code=BINARY @code", db, tx);
        Parameters(find, ("code", code));
        var result = await find.ExecuteScalarAsync(ct);
        return result == null ? 0 : Convert.ToInt32(result);
    }

    private static async Task<int> Category(MySqlConnection db, MySqlTransaction? tx, string code, bool create, CancellationToken ct)
    {
        if (create)
        {
            using var add = new MySqlCommand("INSERT IGNORE INTO journal_categories(code) VALUES(@code)", db, tx);
            Parameters(add, ("code", code));
            await add.ExecuteNonQueryAsync(ct);
        }

        using var find = new MySqlCommand("SELECT id FROM journal_categories WHERE BINARY code=BINARY @code", db, tx);
        Parameters(find, ("code", code));
        var result = await find.ExecuteScalarAsync(ct);
        return result == null ? 0 : Convert.ToInt32(result);
    }

    private static async Task<int> Label(MySqlConnection db, MySqlTransaction tx, string table, string label, CancellationToken ct)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(label));
        using var add = new MySqlCommand($"INSERT IGNORE INTO {table}(label,label_hash) VALUES(@label,@hash)", db, tx);
        Parameters(add, ("label", label), ("hash", hash));
        await add.ExecuteNonQueryAsync(ct);
        using var find = new MySqlCommand($"SELECT id,label FROM {table} WHERE label_hash=@hash", db, tx);
        Parameters(find, ("hash", hash));
        await using var r = await find.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        if (r.GetString(1) != label)
            throw new InvalidOperationException("Коллизия хеша справочника; значение не объединено.");
        return r.GetInt32(0);
    }

    public async Task<string?> ValidateReferences(AccidentInput a, CancellationToken ct)
    {
        await using var db = await Open(ct);
        if (await Code(db, null, "journal_directions", a.Direction, false, ct) == 0)
            return "Выберите направление из справочника.";
        if (!string.IsNullOrWhiteSpace(a.Center) && await Code(db, null, "journal_centers", a.Center, false, ct) == 0)
            return "Выберите ТЦ из справочника или оставьте неизвестным.";
        if (await Code(db, null, "journal_alarm_types", a.AlarmType, false, ct) == 0)
            return "Выберите тип аварии из справочника.";
        foreach (var n in Networks(a.NetworkType))
            if (await Code(db, null, "journal_network_types", n, false, ct) == 0)
                return "Выберите тип сети из справочника.";
        if (await Category(db, null, a.CategoryCode ?? a.Category!.Value.ToString(), false, ct) == 0)
            return "Выберите исходный код категории из справочника.";
        return null;
    }

    public async Task<int> Save(int id, AccidentInput a, int userId, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (id != 0)
        {
            using var check = new MySqlCommand("SELECT version,time_basis FROM journal_accidents WHERE id=@id FOR UPDATE", db, tx);
            Parameters(check, ("id", id));
            await using var r = await check.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return 0;
            if (r.GetInt32(0) != a.Version)
                return -1;
            if (r.GetString(1) != a.TimeBasis)
                return -2;
        }

        id = await Write(db, tx, id, a, userId, null, false, ct);
        await Audit(db, tx, userId, id, a.Version == 0 ? "create" : "update", ct);
        await tx.CommitAsync(ct);
        return id;
    }

    private static async Task<int> Write(MySqlConnection db, MySqlTransaction tx, int id, AccidentInput a, int userId, int? sourceRow, bool createCodes, CancellationToken ct)
    {
        int direction = await Code(db, tx, "journal_directions", a.Direction, createCodes, ct);
        int? center = string.IsNullOrWhiteSpace(a.Center) ? null : await Code(db, tx, "journal_centers", a.Center, createCodes, ct);
        int category = await Category(db, tx, a.CategoryCode ?? a.Category!.Value.ToString(), createCodes, ct);
        int alarm = await Code(db, tx, "journal_alarm_types", a.AlarmType, createCodes, ct);
        int element = await Label(db, tx, "journal_service_elements", a.ServiceElement, ct);
        int? ticket = string.IsNullOrWhiteSpace(a.Pw) ? null : await Label(db, tx, "journal_tickets", a.Pw, ct);
        const string fields = "direction_id=@dir,center_id=@center,category_id=@cat,alarm_type_id=@alarm,service_element_id=@element,ticket_id=@ticket,description=@description,begin_at=@begin,end_at=@end,time_basis=@basis,station_count=@count,is_sector=@sector,battery_seconds=@battery,generator_seconds=@generator,downtime_seconds=@downtime,notified=@notified,notes=@notes";
        using var cmd = new MySqlCommand(id == 0 ? "INSERT INTO journal_accidents SET " + fields + ",created_by=@user,source_row_id=@source" : "UPDATE journal_accidents SET " + fields + ",version=version+1 WHERE id=@id", db, tx);
        Parameters(cmd, ("dir", direction), ("center", center), ("cat", category), ("alarm", alarm), ("element", element), ("ticket", ticket), ("description", a.Description), ("begin", a.StoredBegin), ("end", a.StoredEnd), ("basis", a.TimeBasis), ("count", a.StationCount), ("sector", a.IsSector), ("battery", a.BatterySeconds), ("generator", a.GeneratorSeconds), ("downtime", a.DowntimeSeconds), ("notified", a.Notified ?? ""), ("notes", a.Notes ?? ""), ("user", userId), ("source", sourceRow), ("id", id));
        await cmd.ExecuteNonQueryAsync(ct);
        if (id == 0)
            id = (int)cmd.LastInsertedId;
        using (var clear = new MySqlCommand("DELETE FROM journal_accident_networks WHERE accident_id=@id;DELETE FROM journal_accident_equipment WHERE accident_id=@id", db, tx))
        {
            Parameters(clear, ("id", id));
            await clear.ExecuteNonQueryAsync(ct);
        }

        foreach (var n in Networks(a.NetworkType).Distinct())
        {
            int network = await Code(db, tx, "journal_network_types", n, createCodes, ct);
            using var link = new MySqlCommand("INSERT INTO journal_accident_networks VALUES(@id,@n)", db, tx);
            Parameters(link, ("id", id), ("n", network));
            await link.ExecuteNonQueryAsync(ct);
        }

        foreach (var e in (a.Equipment ?? "").Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x != "").Distinct(StringComparer.Ordinal))
        {
            int model = await Label(db, tx, "journal_equipment_models", e, ct);
            using var link = new MySqlCommand("INSERT INTO journal_accident_equipment VALUES(@id,@e)", db, tx);
            Parameters(link, ("id", id), ("e", model));
            await link.ExecuteNonQueryAsync(ct);
        }

        return id;
    }

    public async Task<int> Delete(int id, int version, int userId, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        using var check = new MySqlCommand("SELECT version FROM journal_accidents WHERE id=@id FOR UPDATE", db, tx);
        Parameters(check, ("id", id));
        var current = await check.ExecuteScalarAsync(ct);
        if (current == null)
            return 0;
        if (Convert.ToInt32(current) != version)
            return -1;
        using var cmd = new MySqlCommand("DELETE FROM journal_accidents WHERE id=@id", db, tx);
        Parameters(cmd, ("id", id));
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit(db, tx, userId, id, "delete", ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<object> AuditLog(int page, int pageSize, CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var count = new MySqlCommand("SELECT COUNT(*) FROM accident_audit", db);
        int total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        using var cmd = new MySqlCommand("SELECT a.id,a.accident_id,a.action,a.occurred_at,u.username FROM accident_audit a JOIN users u ON u.user_id=a.user_id ORDER BY a.id DESC LIMIT @take OFFSET @skip", db);
        Parameters(cmd, ("take", pageSize), ("skip", (page - 1) * pageSize));
        var rows = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new { id = r.GetInt32(0), accidentId = r.IsDBNull(1) ? (int? )null : r.GetInt32(1), action = r.GetString(2), occurredAt = DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc), username = r.GetString(4) });
        return new
        {
            total,
            page,
            pageSize,
            items = rows
        };
    }

    public async Task<JournalUser?> User(int id, CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var cmd = new MySqlCommand("SELECT u.user_id,u.username,a.role,a.active,a.version FROM users u JOIN journal_user_access a ON a.user_id=u.user_id WHERE u.user_id=@id", db);
        Parameters(cmd, ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4)) : null;
    }

    public async Task<(JournalUser? User, string? Hash)> LoginUser(string name, CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var cmd = new MySqlCommand("SELECT u.user_id,u.username,a.role,a.active,a.version,u.password_hash FROM users u JOIN journal_user_access a ON a.user_id=u.user_id WHERE u.username=@name", db);
        Parameters(cmd, ("name", name));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4)), r.GetString(5)) : (null, null);
    }

    public async Task<List<JournalUser>> Users(CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var cmd = new MySqlCommand("SELECT u.user_id,u.username,a.role,a.active,a.version FROM users u JOIN journal_user_access a ON a.user_id=u.user_id ORDER BY u.user_id", db);
        var rows = new List<JournalUser>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4)));
        return rows;
    }

    public async Task<int> CreateUser(CreateJournalUser input, int actor, CancellationToken ct)
    {
        string hash = BCrypt.Net.BCrypt.HashPassword(input.Password, workFactor: 11);
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        using var cmd = new MySqlCommand("INSERT INTO users(username,password_hash) VALUES(@name,@hash)", db, tx);
        Parameters(cmd, ("name", input.Username), ("hash", hash));
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (MySqlException e)when (e.Number == 1062)
        {
            return -1;
        }

        int id = (int)cmd.LastInsertedId;
        using var access = new MySqlCommand("INSERT INTO journal_user_access(user_id,role) VALUES(@id,@role)", db, tx);
        Parameters(access, ("id", id), ("role", input.Role));
        await access.ExecuteNonQueryAsync(ct);
        await Audit(db, tx, actor, null, "user_create:" + id, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<int> UpdateAccess(int id, UpdateJournalAccess input, int actor, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        using var check = new MySqlCommand("SELECT version FROM journal_user_access WHERE user_id=@id FOR UPDATE", db, tx);
        Parameters(check, ("id", id));
        var version = await check.ExecuteScalarAsync(ct);
        if (version == null)
            return 0;
        if (Convert.ToInt32(version) != input.Version)
            return -1;
        using var cmd = new MySqlCommand("UPDATE journal_user_access SET role=@role,active=@active,version=version+1 WHERE user_id=@id", db, tx);
        Parameters(cmd, ("role", input.Role), ("active", input.Active), ("id", id));
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit(db, tx, actor, null, "user_access:" + id, ct);
        await tx.CommitAsync(ct);
        return id;
    }
}
