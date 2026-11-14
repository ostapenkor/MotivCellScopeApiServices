using System.Data;
using MotivCellScopeApiServices.Contracts;
using MySqlConnector;

namespace MotivCellScopeApiServices.Data;
public partial class AccidentStore
{
    public async Task<Dictionary<string, object?>> Options(CancellationToken ct)
    {
        await using var db = await Open(ct);
        async Task<List<string>> Codes(string sql)
        {
            using var cmd = new MySqlCommand(sql, db);
            var values = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                values.Add(r.GetString(0));
            return values;
        }

        var directions = await Codes("SELECT code FROM journal_directions ORDER BY code");
        var centers = await Codes("SELECT code FROM journal_centers ORDER BY code");
        var networks = await Codes("SELECT code FROM journal_network_types UNION SELECT 'LTE/GSM' ORDER BY 1");
        var alarms = await Codes("SELECT code FROM journal_alarm_types ORDER BY code");
        var categories = new List<object>();
        using (var cmd = new MySqlCommand("SELECT code,mapped_category FROM journal_categories ORDER BY mapped_category IS NULL,mapped_category,code", db))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                categories.Add(new { code = r.GetString(0), label = r.GetString(0), mappedCategory = r.IsDBNull(1) ? (int? )null : r.GetInt32(1) });
        }

        object? dataRange = null;
        using (var cmd = new MySqlCommand("SELECT MIN(begin_at),MAX(begin_at) FROM journal_accidents WHERE source_row_id IS NOT NULL", db))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            if (!r.IsDBNull(0))
                dataRange = new
                {
                    from = r.GetDateTime(0).ToString("yyyy-MM-dd"),
                    to = r.GetDateTime(1).AddDays(1).ToString("yyyy-MM-dd")
                };
        }

        return new Dictionary<string, object?>
        {
            ["directions"] = directions,
            ["centers"] = centers,
            ["networkTypes"] = networks,
            ["alarmTypes"] = alarms,
            ["categories"] = categories,
            ["dataRange"] = dataRange,
            ["serverTimeUtc"] = DateTimeOffset.UtcNow
        };
    }

    public async Task<object> Dashboard(AccidentFilter f, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        int total, open, historicalOpen, unmapped, unknownCount, unknownDowntime, excluded;
        decimal knownTotal;
        using (var cmd = new MySqlCommand("SELECT COUNT(*),COALESCE(SUM(end_at IS NULL),0),COALESCE(SUM(end_at IS NULL AND source_row_id IS NOT NULL),0),COALESCE(SUM(KATEG IS NULL),0),COALESCE(SUM(station_count IS NULL AND is_sector=0),0),COALESCE(SUM(downtime_seconds IS NULL),0),COALESCE(SUM(total_downtime_seconds),0),COALESCE(SUM(total_downtime_seconds IS NULL),0) FROM journal_report a WHERE " + Where, db, tx))
        {
            FilterParameters(cmd, f);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            total = r.GetInt32(0);
            open = r.GetInt32(1);
            historicalOpen = r.GetInt32(2);
            unmapped = r.GetInt32(3);
            unknownCount = r.GetInt32(4);
            unknownDowntime = r.GetInt32(5);
            knownTotal = r.GetDecimal(6);
            excluded = r.GetInt32(7);
        }

        async Task<List<object>> Group(string expression, string order)
        {
            using var cmd = new MySqlCommand($"SELECT {expression} AS label,COUNT(*) AS n FROM journal_report a WHERE " + Where + $" GROUP BY {expression} ORDER BY {order}", db, tx);
            FilterParameters(cmd, f);
            var result = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                result.Add(new { label = r.GetString(0), count = r.GetInt32(1) });
            return result;
        }

        var byDirection = await Group("NAPR", "n DESC,label");
        var byCenter = await Group("IF(TEHC='','Не указан',TEHC)", "n DESC,label");
        var byAlarmType = await Group("ALARM_TYPE", "n DESC,label");
        var byCategory = await Group("category_code", "n DESC,label");
        var byMonth = await Group("DATE_FORMAT(begin_at,'%Y-%m')", "label");
        object source;
        using (var cmd = new MySqlCommand("SELECT (SELECT COUNT(*) FROM journal_import_rows WHERE batch_id IN(SELECT ir.batch_id FROM journal_import_rows ir JOIN journal_accidents ja ON ja.source_row_id=ir.id)),(SELECT COUNT(*) FROM journal_import_batches WHERE id IN(SELECT ir.batch_id FROM journal_import_rows ir JOIN journal_accidents ja ON ja.source_row_id=ir.id)),(SELECT COUNT(*) FROM journal_accidents WHERE source_row_id IS NOT NULL),MIN(begin_at),MAX(begin_at) FROM journal_accidents WHERE source_row_id IS NOT NULL", db, tx))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            source = new
            {
                rows = r.GetInt32(0),
                sourceRows = r.GetInt32(0),
                batches = r.GetInt32(1),
                eventRows = r.GetInt32(2),
                unmappedRows = unmapped,
                minBegin = r.IsDBNull(3) ? null : Local(r.GetDateTime(3)),
                maxBegin = r.IsDBNull(4) ? null : Local(r.GetDateTime(4)),
                timeZone = (string? )null
            };
        }

        await tx.CommitAsync(ct);
        return new
        {
            total,
            open,
            closed = total - open,
            historicalOpen,
            liveOpen = open - historicalOpen,
            unmappedCategories = unmapped,
            unknownStationCount = unknownCount,
            unknownDowntime,
            knownTotalDowntimeSeconds = knownTotal,
            excludedDowntimeRows = excluded,
            byDirection,
            byCenter,
            byAlarmType,
            byCategory,
            byMonth,
            source,
            serverTimeUtc = DateTimeOffset.UtcNow
        };
    }
}
