using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Data;

namespace MotivCellScopeApiServices.Controllers;
[ApiController, Authorize, Route("api/accidents")]
public class AccidentsController : ControllerBase
{
    private readonly AccidentStore store;
    public AccidentsController(AccidentStore store) => this.store = store;
    private int Actor => int.Parse(User.FindFirstValue("uid")!);

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var result = await store.Options(ct);
        result["permissions"] = new
        {
            canWrite = User.IsInRole("admin") || User.IsInRole("duty"),
            canDelete = User.IsInRole("admin"),
            canManageUsers = User.IsInRole("admin")
        };
        return Ok(result);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] AccidentFilter filter, CancellationToken ct) => Ok(await store.Dashboard(filter, ct));
    [HttpPost("import"), Authorize(Roles = "admin"), RequestSizeLimit(20971520)]
    public async Task<IActionResult> Import(ImportBatchInput input, CancellationToken ct)
    {
        try
        {
            return Ok(await store.Import(input, Actor, ct));
        }
        catch (ImportInProgressException e)
        {
            return Conflict(new ProblemDetails { Title = e.Message, Status = 409 });
        }
        catch (ValidationException e)
        {
            return BadRequest(new ProblemDetails { Title = e.Message, Status = 400 });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AccidentFilter filter, CancellationToken ct) => Ok(await store.List(filter, false, ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var item = await store.Get(id, ct);
        return item == null ? NotFound(new ProblemDetails { Title = "Авария не найдена", Status = 404 }) : Ok(item);
    }

    [HttpPost, Authorize(Roles = "admin,duty")]
    public async Task<IActionResult> Create(AccidentInput input, CancellationToken ct)
    {
        var error = await store.ValidateReferences(input, ct);
        if (error != null)
            return BadRequest(new ProblemDetails { Title = error, Status = 400 });
        int id = await store.Save(0, input, Actor, ct);
        return CreatedAtAction(nameof(Get), new { id }, await store.Get(id, ct));
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Roles = "admin,duty")]
    public async Task<IActionResult> Update(int id, AccidentInput input, CancellationToken ct)
    {
        if (input.Version < 1)
        {
            ModelState.AddModelError("version", "Для изменения нужна версия записи.");
            return ValidationProblem(ModelState);
        }

        var error = await store.ValidateReferences(input, ct);
        if (error != null)
            return BadRequest(new ProblemDetails { Title = error, Status = 400 });
        int result = await store.Save(id, input, Actor, ct);
        if (result == -2)
            return BadRequest(new ProblemDetails { Title = "Основание времени исходной записи менять нельзя.", Status = 400 });
        if (result == 0)
            return NotFound(new ProblemDetails { Title = "Авария не найдена", Status = 404 });
        if (result == -1)
            return Conflict(new ProblemDetails { Title = "Запись уже изменена коллегой. Откройте свежую версию; ваш ввод не сохранён.", Status = 409 });
        return Ok(await store.Get(id, ct));
    }

    [HttpDelete("{id:int:min(1)}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id, [FromQuery, Range(1, int.MaxValue)] int version, CancellationToken ct)
    {
        if (version < 1)
            return BadRequest(new ProblemDetails { Title = "Нужна версия удаляемой записи.", Status = 400 });
        int result = await store.Delete(id, version, Actor, ct);
        return result == 0 ? NotFound(new ProblemDetails { Title = "Авария не найдена", Status = 404 }) : result == -1 ? Conflict(new ProblemDetails { Title = "Запись изменена. Проверьте актуальную версию перед удалением.", Status = 409 }) : NoContent();
    }

    [HttpGet("audit"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Audit([FromQuery, Range(1, 100000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 25, CancellationToken ct = default) => Ok(await store.AuditLog(page, pageSize, ct));
    [HttpGet("export.csv")]
    public async Task<IActionResult> Export([FromQuery] AccidentFilter filter, CancellationToken ct)
    {
        var data = await store.List(filter, true, ct);
        var csv = new StringBuilder("id;pw;direction;center;category;networkType;serviceElement;stationCount;equipment;alarmType;description;beginTime;endTime;battery;generator;duration;downtime;totalDowntime;notified;notes;timeBasis;sourceSheet;sourceRow;sourceSnapshotAtLocal\r\n");
        foreach (var a in data.Items)
        {
            var source = a.Source == null ? default(JsonElement) : JsonSerializer.SerializeToElement(a.Source);
            string Meta(string field) => source.ValueKind == JsonValueKind.Object && source.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
            var values = new[]
            {
                a.Id.ToString(CultureInfo.InvariantCulture),
                a.Pw,
                a.Direction,
                a.Center,
                a.CategoryCode ?? a.Category.ToString(),
                a.NetworkType,
                a.ServiceElement,
                a.IsSector ? "С" : a.StationCount.ToString(),
                a.Equipment,
                a.AlarmType,
                a.Description,
                a.BeginTimeLocal ?? a.BeginTime!.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                a.EndTimeLocal ?? a.EndTime?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "НВ",
                Format(a.BatterySeconds),
                Format(a.GeneratorSeconds),
                Format(a.DurationSeconds),
                Format(a.DowntimeSeconds),
                Format(a.TotalDowntimeSeconds),
                a.Notified,
                a.Notes,
                a.TimeBasis,
                Meta("sheet"),
                Meta("row"),
                Meta("snapshotAtLocal")
            };
            csv.AppendJoin(";", values.Select(Escape)).Append("\r\n");
        }

        var utf8 = new UTF8Encoding(true);
        return File(utf8.GetPreamble().Concat(utf8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"accidents_{filter.UtcFrom:yyyyMMdd}_{filter.UtcTo:yyyyMMdd}.csv");
    }

    private static string Format(decimal? value) => value.HasValue ? JournalOptions.FormatSeconds(value.Value) : "";
    private static string Escape(string? value)
    {
        value ??= "";
        string trimmed = value.TrimStart();
        if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || value[0] is '\t' or '\r' or '\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
