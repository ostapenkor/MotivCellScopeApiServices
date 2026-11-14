using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MotivCellScopeApiServices.Contracts;
using MySqlConnector;

namespace MotivCellScopeApiServices.Data;
public partial class AccidentStore
{
    private static readonly JsonSerializerOptions ImportJson = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AccidentSourceSheets = new(StringComparer.Ordinal)
    {
        "Аварии на сети СС",
        "Аварии на сети ФС"
    };
    // Один пакет, его исходные строки, справочники и факты — одна транзакция.
    // Advisory lock сериализует импорты в этой БД; UNIQUE(file_hash) остаётся
    // дополнительной защитой от повторного импорта на уровне хранения.
    public async Task<ImportResult> Import(ImportBatchInput input, int userId, CancellationToken ct)
    {
        ValidateImportMetadata(input);
        string hash = input.FileHash.ToLowerInvariant();
        await using var db = await Open(ct);
        string lockName = "journal-import:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(db.Database))).Substring(0, 40);
        bool locked = false;
        try
        {
            using (var acquire = new MySqlCommand("SELECT GET_LOCK(@name,30)", db))
            {
                Parameters(acquire, ("name", lockName));
                locked = Convert.ToInt32(await acquire.ExecuteScalarAsync(ct)) == 1;
            }

            if (!locked)
                throw new ImportInProgressException();
            await using var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            using (var existing = new MySqlCommand("SELECT result_json FROM journal_import_batches WHERE file_hash=@hash", db, tx))
            {
                Parameters(existing, ("hash", hash));
                var saved = await existing.ExecuteScalarAsync(ct);
                if (saved != null)
                {
                    if (saved == DBNull.Value)
                        throw new ValidationException("Этот файл уже зарегистрирован без сохранённой статистики. Требуется проверка прежнего импорта; повторное создание строк запрещено.");
                    var result = JsonSerializer.Deserialize<ImportResult>(Convert.ToString(saved)!, ImportJson) ?? throw new InvalidOperationException("Не удалось прочитать сохранённый результат импорта.");
                    await tx.CommitAsync(ct);
                    return result with
                    {
                        AlreadyImported = true
                    };
                }
            }

            var batchSnapshot = Snapshot(input.SnapshotAtLocal, input.SnapshotAt);
            using var batch = new MySqlCommand("INSERT INTO journal_import_batches(file_hash,source_name,time_zone,snapshot_at,imported_at,imported_by) " + "VALUES(@hash,@name,@zone,@snapshot,UTC_TIMESTAMP(3),@user)", db, tx);
            Parameters(batch, ("hash", hash), ("name", input.SourceName), ("zone", input.TimeZone), ("snapshot", batchSnapshot.Value), ("user", userId));
            await batch.ExecuteNonQueryAsync(ct);
            int batchId = (int)batch.LastInsertedId;
            var sheetSnapshots = new Dictionary<string, (DateTime? Value, string? Basis)>(StringComparer.Ordinal);
            foreach (var sheet in input.Sheets)
            {
                var snapshot = sheet.SnapshotAtLocal == null && sheet.SnapshotAt == null ? batchSnapshot : Snapshot(sheet.SnapshotAtLocal, sheet.SnapshotAt);
                sheetSnapshots.Add(sheet.Name, snapshot);
                using var cmd = new MySqlCommand("INSERT INTO journal_import_sheets(batch_id,sheet_name,snapshot_at) VALUES(@batch,@sheet,@snapshot)", db, tx);
                Parameters(cmd, ("batch", batchId), ("sheet", sheet.Name), ("snapshot", snapshot.Value));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await ImportReferences(db, tx, "journal_directions", input.References.Directions, ct);
            await ImportReferences(db, tx, "journal_centers", input.References.Centers, ct);
            int promoted = 0, rejected = 0, deferred = 0, overview = 0, issuesRows = 0;
            foreach (var row in input.Rows)
            {
                var issues = new List<string>(row.Issues);
                bool validEvent = row.Kind == "event";
                if (row.Kind == "event")
                {
                    var errors = EventImportErrors(row, input.TimeZone, sheetSnapshots[row.Sheet]);
                    issues.AddRange(errors);
                    validEvent = errors.Count == 0;
                }

                if (issues.Count > 0)
                    issuesRows++;
                string issuesJson = JsonSerializer.Serialize(issues, ImportJson);
                if (Encoding.UTF8.GetByteCount(issuesJson) > 65535)
                    throw new ValidationException("Список замечаний одной строки превышает ёмкость TEXT. Пакет не сохранён; сократите замечания без удаления исходных данных.");
                using var source = new MySqlCommand("INSERT INTO journal_import_rows(batch_id,sheet_name,row_number,kind,raw_json,issues_json) " + "VALUES(@batch,@sheet,@row,@kind,@raw,@issues)", db, tx);
                Parameters(source, ("batch", batchId), ("sheet", row.Sheet), ("row", row.Row), ("kind", row.Kind), ("raw", row.RawValues.GetRawText()), ("issues", issuesJson));
                await source.ExecuteNonQueryAsync(ct);
                int sourceId = (int)source.LastInsertedId;
                if (validEvent)
                {
                    await Write(db, tx, 0, row.Event!, userId, sourceId, true, ct);
                    promoted++;
                }
                else if (row.Kind == "event")
                    rejected++;
                else if (row.Kind == "overview")
                    overview++;
                else
                    deferred++; // deferred + reference: сохранены, но не превращены в аварии.
            }

            var completed = new ImportResult(batchId, false, input.Rows.Count, promoted, rejected, deferred, overview, issuesRows);
            using (var complete = new MySqlCommand("UPDATE journal_import_batches SET result_json=@result WHERE id=@id AND result_json IS NULL", db, tx))
            {
                Parameters(complete, ("result", JsonSerializer.Serialize(completed, ImportJson)), ("id", batchId));
                await complete.ExecuteNonQueryAsync(ct);
            }

            // Одна запись аудита для атомарного импорта; исходные ячейки в аудит/ответ не попадают.
            await Audit(db, tx, userId, null, "import:" + batchId, ct);
            await tx.CommitAsync(ct);
            return completed;
        }
        finally
        {
            if (locked && db.State == ConnectionState.Open)
            {
                using var release = new MySqlCommand("SELECT RELEASE_LOCK(@name)", db);
                Parameters(release, ("name", lockName));
                await release.ExecuteScalarAsync(CancellationToken.None);
            }
        }
    }

    // Только безопасные метаданные; raw_json доступным ответом этого метода не является.
    public async Task<object> Imports(CancellationToken ct)
    {
        await using var db = await Open(ct);
        using var cmd = new MySqlCommand("SELECT id,source_name,time_zone,snapshot_at,imported_at,result_json FROM journal_import_batches ORDER BY id DESC LIMIT 100", db);
        var items = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            items.Add(new { batchId = r.GetInt32(0), sourceName = r.GetString(1), timeZone = r.IsDBNull(2) ? null : r.GetString(2), snapshotAtLocal = r.IsDBNull(3) ? null : Local(r.GetDateTime(3)), importedAt = Utc(r.GetDateTime(4)), result = r.IsDBNull(5) ? null : JsonSerializer.Deserialize<ImportResult>(r.GetString(5), ImportJson) });
        }

        return new
        {
            items
        };
    }

    private static async Task ImportReferences(MySqlConnection db, MySqlTransaction tx, string table, List<ImportCodeInput> values, CancellationToken ct)
    {
        // Имя таблицы приходит только из двух фиксированных вызовов выше.
        foreach (var value in values)
        {
            using var cmd = new MySqlCommand($"INSERT INTO {table}(code,label,external_code) VALUES(@code,@label,@external) " + "ON DUPLICATE KEY UPDATE label=IF(BINARY label=BINARY code,VALUES(label),label)," + "external_code=COALESCE(external_code,VALUES(external_code))", db, tx);
            Parameters(cmd, ("code", value.Code), ("label", value.Label), ("external", value.ExternalCode));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static List<string> EventImportErrors(ImportRowInput row, string? zone, (DateTime? Value, string? Basis) snapshot)
    {
        var errors = new List<string>();
        if (!AccidentSourceSheets.Contains(row.Sheet))
            errors.Add("В текущем этапе в аварии повышаются только листы СС и ФС; строка сохранена как источник.");
        if (row.Event == null)
        {
            errors.Add("Строка не разобрана в событие; исходные значения сохранены.");
            return errors;
        }

        var a = row.Event;
        var validation = new List<ValidationResult>();
        Validator.TryValidateObject(a, new ValidationContext(a), validation, validateAllProperties: true);
        errors.AddRange(validation.Select(v => v.ErrorMessage ?? "Неверное поле события."));
        if (zone == null && a.TimeBasis != "source_local")
            errors.Add("Часовой пояс источника неизвестен: нужно source_local и локальные даты без объявления UTC.");
        if (snapshot.Basis != null && a.TimeBasis != snapshot.Basis)
            errors.Add("Основание времени события не совпадает с основанием даты снимка листа.");
        foreach (var(field, code)in new[]
        {
            ("direction", a.Direction),
            ("center", a.Center),
            ("alarmType", a.AlarmType)
        }

        )
            if (code != null && (code.Length > 64 || code != code.Trim()))
                errors.Add($"{field}: код должен помещаться в 64 символа и не содержать крайних пробелов.");
        foreach (var code in Networks(a.NetworkType ?? ""))
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64 || code != code.Trim())
                errors.Add("networkType: каждый код должен помещаться в 64 символа и не содержать крайних пробелов.");
        if (a.CategoryCode != null && a.Category.HasValue && a.CategoryCode != a.Category.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            errors.Add("category и categoryCode противоречат друг другу; автоматическое сопоставление исходной категории не выполнено.");
        foreach (var(field, seconds)in new[]
        {
            ("batterySeconds", a.BatterySeconds),
            ("generatorSeconds", a.GeneratorSeconds),
            ("downtimeSeconds", a.DowntimeSeconds)
        }

        )
            if (seconds.HasValue && decimal.Round(seconds.Value, 3) != seconds.Value)
                errors.Add($"{field}: точность хранения — миллисекунды; исходное значение не округлено и оставлено в источнике.");
        if (validation.Count == 0)
        {
            if (a.StoredBegin.Ticks % TimeSpan.TicksPerMillisecond != 0 || a.StoredEnd?.Ticks % TimeSpan.TicksPerMillisecond != 0 && a.StoredEnd.HasValue)
                errors.Add("Дата точнее миллисекунды не округляется автоматически; уточните значение для повышения строки.");
            if (a.StoredEnd == null && snapshot.Value.HasValue && snapshot.Value.Value < a.StoredBegin)
                errors.Add("Дата снимка раньше начала незавершённой аварии; длительность по такому снимку не определена.");
        }

        return errors;
    }

    private static (DateTime? Value, string? Basis) Snapshot(string? local, DateTimeOffset? utc)
    {
        if (local != null)
        {
            AccidentInput.TryLocal(local, out var value);
            return (DateTime.SpecifyKind(value, DateTimeKind.Unspecified), "source_local");
        }

        return utc.HasValue ? (utc.Value.UtcDateTime, "utc") : (null, null);
    }

    private static void ValidateImportMetadata(ImportBatchInput input)
    {
        if (input == null)
            throw new ValidationException("Нужно тело пакета импорта.");
        var errors = new List<string>();
        void Object(object? value, string path)
        {
            if (value == null)
            {
                errors.Add(path + ": отсутствует.");
                return;
            }

            var results = new List<ValidationResult>();
            Validator.TryValidateObject(value, new ValidationContext(value), results, true);
            errors.AddRange(results.Select(r => path + ": " + r.ErrorMessage));
        }

        void CheckSnapshot(string? local, DateTimeOffset? utc, string path)
        {
            if (local != null && utc.HasValue)
                errors.Add(path + ": передайте snapshotAtLocal или snapshotAt, но не оба.");
            if (local != null && (!AccidentInput.TryLocal(local, out var parsed) || parsed.Ticks % TimeSpan.TicksPerMillisecond != 0))
                errors.Add(path + ": snapshotAtLocal должен быть локальной ISO-датой без смещения, точность не выше миллисекунд.");
            if (utc.HasValue && (utc.Value.Year < 1000 || utc.Value.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond != 0))
                errors.Add(path + ": snapshotAt не помещается в DATETIME(3).");
            if (input.TimeZone == null && utc.HasValue)
                errors.Add(path + ": неизвестный часовой пояс источника не позволяет объявить снимок UTC.");
        }

        Object(input, "batch");
        if (input.TimeZone != null && (string.IsNullOrWhiteSpace(input.TimeZone) || input.TimeZone != input.TimeZone.Trim()))
            errors.Add("timeZone: при неизвестном часовом поясе передайте null.");
        CheckSnapshot(input.SnapshotAtLocal, input.SnapshotAt, "batch");
        if (input.Rows == null || input.Sheets == null || input.References == null)
            errors.Add("rows, sheets и references не должны быть null.");
        if (input.Rows?.Count > 50000)
            errors.Add("Пакет превышает 50000 исходных строк.");
        var sheets = new HashSet<string>(StringComparer.Ordinal);
        var uniqueSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in input.Sheets ?? new())
        {
            Object(sheet, "sheet");
            if (sheet == null)
                continue;
            if (sheet.Name == null || !uniqueSheetNames.Add(sheet.Name))
                errors.Add("Повторное или отсутствующее имя листа.");
            else
                sheets.Add(sheet.Name);
            CheckSnapshot(sheet.SnapshotAtLocal, sheet.SnapshotAt, "sheet");
        }

        var rows = new HashSet<(string Sheet, int Row)>();
        foreach (var row in input.Rows ?? new())
        {
            Object(row, "row");
            if (row == null)
                continue;
            if (row.Sheet == null || !sheets.Contains(row.Sheet))
                errors.Add("Строка ссылается на необъявленный лист.");
            if (!rows.Add((row.Sheet ?? "", row.Row)))
                errors.Add("Пакет содержит повторную координату лист/строка.");
            if (row.RawValues.ValueKind == JsonValueKind.Undefined)
                errors.Add("У каждой строки должны быть rawValues.");
            if (row.Issues == null || row.Issues.Any(i => i == null))
                errors.Add("issues должен быть массивом строк.");
            else if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(row.Issues, ImportJson)) > 65535)
                errors.Add("Замечания строки превышают ёмкость TEXT.");
        }

        if (input.References != null)
        {
            if (input.References.Directions == null || input.References.Centers == null)
                errors.Add("references.directions и references.centers не должны быть null.");
            foreach (var code in (input.References.Directions ?? new()).Concat(input.References.Centers ?? new()))
            {
                Object(code, "reference");
                if (code != null && (string.IsNullOrWhiteSpace(code.Code) || code.Code != code.Code.Trim()))
                    errors.Add("Код справочника не должен быть пустым или содержать крайние пробелы.");
            }
        }

        if (errors.Count != 0)
            throw new ValidationException(string.Join(" ", errors.Distinct()));
    }
}
