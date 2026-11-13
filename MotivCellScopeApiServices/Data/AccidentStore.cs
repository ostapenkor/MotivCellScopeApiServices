using System.Data;
using MotivCellScopeApiServices.Contracts;
using MySqlConnector;

namespace MotivCellScopeApiServices.Data;
// Параметризованные SQL-запросы нового модуля. Legacy EF-модель не меняется.
public class AccidentStore
{
    private readonly string connectionString;
    public AccidentStore(IConfiguration config) => connectionString = config.GetConnectionString("Journal")!;
    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        var db = new MySqlConnection(connectionString);
        try
        {
            await db.OpenAsync(ct);
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
            cmd.Parameters.AddWithValue("@" + p.Name, p.Value);
    }

    private const string Columns = "a.*,NULLIF(a.END_TIME,'0000-00-00 00:00:00') AS real_end,u.username AS author";
    private static AccidentDto Read(MySqlDataReader r, DateTimeOffset now)
    {
        var begin = new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime("BEGIN_TIME"), DateTimeKind.Utc));
        DateTimeOffset? end = r.IsDBNull(r.GetOrdinal("real_end")) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime("real_end"), DateTimeKind.Utc));
        int number = r.GetInt32("NUMB_RBS");
        return new AccidentDto
        {
            Id = r.GetInt32("id"),
            Pw = r.GetString("PW"),
            Direction = r.GetString("NAPR"),
            Center = r.GetString("TEHC"),
            Category = r.GetInt32("KATEG"),
            NetworkType = r.GetString("NETW_TYPE"),
            ServiceElement = r.GetString("SERV_ELEM"),
            IsSector = number == -1,
            StationCount = number == -1 ? null : number,
            Equipment = r.GetString("EQIP_TYPE"),
            AlarmType = r.GetString("ALARM_TYPE"),
            Description = r.GetString("DESCR"),
            BeginTime = begin,
            EndTime = end,
            BatterySeconds = (long)r.GetTimeSpan("AKB").TotalSeconds,
            GeneratorSeconds = (long)r.GetTimeSpan("DGU").TotalSeconds,
            DowntimeSeconds = (long)r.GetTimeSpan("PROSTOY").TotalSeconds,
            Notified = r.GetString("ALARM_WHO"),
            Notes = r.GetString("DESC_NOTICE"),
            Version = r.GetInt32("version"),
            CreatedBy = r.GetInt32("created_by"),
            CreatedByName = r.GetString("author"),
            DurationSeconds = (long)Math.Max(0, ((end ?? now) - begin).TotalSeconds),
            TotalDowntimeSeconds = number == -1 ? null : (long)r.GetTimeSpan("PROSTOY").TotalSeconds * number,
            ServerTimeUtc = now
        };
    }

    private const string Where = "a.BEGIN_TIME < @to AND (a.END_TIME='0000-00-00 00:00:00' OR a.END_TIME>@from) " + "AND (@direction IS NULL OR a.NAPR=@direction) AND (@center IS NULL OR a.TEHC=@center) " + "AND (@category IS NULL OR a.KATEG=@category) AND (@network IS NULL OR a.NETW_TYPE=@network) " + "AND (@status='all' OR (@status='open' AND a.END_TIME='0000-00-00 00:00:00') OR (@status='closed' AND a.END_TIME<>'0000-00-00 00:00:00')) " + "AND (@search IS NULL OR a.SERV_ELEM LIKE @search ESCAPE '=' OR a.DESCR LIKE @search ESCAPE '=' OR a.PW LIKE @search ESCAPE '=')";
    private static object? Optional(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static void FilterParameters(MySqlCommand cmd, AccidentFilter f)
    {
        string? search = string.IsNullOrWhiteSpace(f.Search) ? null : "%" + f.Search.Replace("=", "==").Replace("%", "=%").Replace("_", "=_") + "%";
        Parameters(cmd, ("from", f.UtcFrom), ("to", f.UtcTo), ("direction", Optional(f.Direction)), ("center", Optional(f.Center)), ("category", f.Category), ("network", Optional(f.NetworkType)), ("status", f.Status), ("search", search));
    }

    public async Task<AccidentPage> List(AccidentFilter f, bool export, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        int total, open, categoryOne;
        using (var count = new MySqlCommand("SELECT COUNT(*),COALESCE(SUM(END_TIME='0000-00-00 00:00:00'),0),COALESCE(SUM(KATEG=1),0) FROM accident_reports a WHERE " + Where, db, tx))
        {
            FilterParameters(count, f);
            await using var r = await count.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            total = r.GetInt32(0);
            open = r.GetInt32(1);
            categoryOne = r.GetInt32(2);
        }

        var now = DateTimeOffset.UtcNow;
        var rows = new List<AccidentDto>();
        using (var cmd = new MySqlCommand("SELECT " + Columns + " FROM accident_reports a JOIN users u ON u.user_id=a.created_by WHERE " + Where + " ORDER BY a.BEGIN_TIME DESC,a.id DESC" + (export ? "" : " LIMIT @take OFFSET @skip"), db, tx))
        {
            FilterParameters(cmd, f);
            if (!export)
                Parameters(cmd, ("take", f.PageSize), ("skip", (f.Page - 1) * f.PageSize));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add(Read(r, now));
        }

        await tx.CommitAsync(ct);
        return new(total, f.Page, f.PageSize, now, new { total, open, closed = total - open, categoryOne }, rows);
    }

    public async Task<AccidentDto?> Get(int id, CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var cmd = new MySqlCommand("SELECT " + Columns + " FROM accident_reports a JOIN users u ON u.user_id=a.created_by WHERE a.id=@id", db);
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

    // 0 = строки нет; -1 = версия устарела; положительное значение = ID.
    public async Task<int> Save(int id, AccidentInput a, int userId, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (id != 0)
        {
            using var check = new MySqlCommand("SELECT version FROM accident_reports WHERE id=@id FOR UPDATE", db, tx);
            Parameters(check, ("id", id));
            var version = await check.ExecuteScalarAsync(ct);
            if (version == null)
                return 0;
            if (Convert.ToInt32(version) != a.Version)
                return -1;
        }

        const string fields = "PW=@pw,NAPR=@direction,TEHC=@center,KATEG=@category,NETW_TYPE=@network,SERV_ELEM=@element,NUMB_RBS=@number,EQIP_TYPE=@equipment,ALARM_TYPE=@type,DESCR=@description,BEGIN_TIME=@begin,END_TIME=COALESCE(@end,'0000-00-00 00:00:00'),AKB=@battery,DGU=@generator,LENGHT=@duration,PROSTOY=@downtime,COMMON_PROSTOY=@total,ALARM_WHO=@notified,DESC_NOTICE=@notes";
        using var cmd = new MySqlCommand(id == 0 ? "INSERT INTO accident_reports SET " + fields + ",created_by=@user" : "UPDATE accident_reports SET " + fields + ",version=version+1 WHERE id=@id", db, tx);
        var now = DateTimeOffset.UtcNow;
        long duration = (long)Math.Max(0, ((a.EndTime ?? now) - a.BeginTime!.Value).TotalSeconds);
        Parameters(cmd, ("pw", a.Pw ?? ""), ("direction", a.Direction), ("center", a.Center), ("category", a.Category), ("network", a.NetworkType), ("element", a.ServiceElement), ("number", a.IsSector ? -1 : a.StationCount), ("equipment", a.Equipment ?? ""), ("type", a.AlarmType), ("description", a.Description), ("begin", a.BeginTime.Value.UtcDateTime), ("end", a.EndTime?.UtcDateTime), ("battery", TimeSpan.FromSeconds(a.BatterySeconds)), ("generator", TimeSpan.FromSeconds(a.GeneratorSeconds)), ("duration", JournalOptions.FormatSeconds(duration)), ("downtime", TimeSpan.FromSeconds(a.DowntimeSeconds)), ("total", a.IsSector ? null : TimeSpan.FromSeconds(a.DowntimeSeconds * a.StationCount!.Value)), ("notified", a.Notified ?? ""), ("notes", a.Notes ?? ""), ("user", userId), ("id", id));
        await cmd.ExecuteNonQueryAsync(ct);
        bool created = id == 0;
        if (created)
            id = (int)cmd.LastInsertedId;
        await Audit(db, tx, userId, id, created ? "create" : "update", ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<int> Delete(int id, int version, int userId, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        using var check = new MySqlCommand("SELECT version FROM accident_reports WHERE id=@id FOR UPDATE", db, tx);
        Parameters(check, ("id", id));
        var current = await check.ExecuteScalarAsync(ct);
        if (current == null)
            return 0;
        if (Convert.ToInt32(current) != version)
            return -1;
        using var cmd = new MySqlCommand("DELETE FROM accident_reports WHERE id=@id", db, tx);
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
