using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using System.Text;

namespace MotivCellScopeApiServices.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Доступно всем авторизованным (инженер, оператор, админ)
public class ReportsController : ControllerBase
{
    private readonly MotivReportDbContext _db;

    public ReportsController(MotivReportDbContext db)
    {
        _db = db;
    }

    // 1. Сводка за период (для карточек на сайте)
    // GET: /api/reports/summary?from=2026-11-01&to=2026-11-30
    [HttpGet("summary")]
    public IActionResult GetSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Incidents.AsQueryable();

        if (from.HasValue) query = query.Where(i => i.BeginTime >= from.Value);
        if (to.HasValue) query = query.Where(i => i.BeginTime <= to.Value);

        var total = query.Count();

        // Группируем по категориям (power, transport и т.д.)
        var byCategory = query
            .GroupBy(i => i.Category)
            .Select(g => new { Category = g.Key.ToString(), Count = g.Count() })
            .ToList();

        // Группируем по важности (low, medium, high, critical)
        var bySeverity = query
            .GroupBy(i => i.Severity)
            .Select(g => new { Severity = g.Key.ToString(), Count = g.Count() })
            .ToList();

        return Ok(new
        {
            total,
            byCategory,
            bySeverity
        });
    }

    // 2. Детальный список за период для таблицы
    // GET: /api/reports/incidents?from=...&to=...
    [HttpGet("incidents")]
    public IActionResult GetIncidentsReport(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.Incidents.AsQueryable();

        if (from.HasValue) query = query.Where(i => i.BeginTime >= from.Value);
        if (to.HasValue) query = query.Where(i => i.BeginTime <= to.Value);

        var total = query.Count();

        // Берем только нужные для отчета поля
        var items = query
            .OrderByDescending(i => i.BeginTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new {
                i.IncidentId,
                i.Pw,
                i.Descr,
                i.BeginTime,
                i.EndTime,
                i.Lenght,
                i.CommonProstoy,
                Category = i.Category.ToString(),
                Severity = i.Severity.ToString()
            })
            .ToList();

        return Ok(new { total, page, pageSize, items });
    }

    // 3. Выгрузка отчета в CSV
    // GET: /api/reports/export?from=...&to=...
    [HttpGet("export")]
    public IActionResult ExportReport([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Incidents.AsQueryable();
        if (from.HasValue) query = query.Where(i => i.BeginTime >= from.Value);
        if (to.HasValue) query = query.Where(i => i.BeginTime <= to.Value);

        var list = query.OrderByDescending(i => i.BeginTime).ToList();

        var csv = new StringBuilder();
        // Заголовки
        csv.AppendLine("ID;PW;Описание;Начало;Окончание;Длительность;Общий простой;Категория;Важность");

        foreach (var i in list)
        {
            // Заменяем возможные точки с запятой в тексте, чтобы не сломать CSV
            var descr = i.Descr?.Replace(";", ",") ?? "";

            csv.AppendLine($"{i.IncidentId};{i.Pw};{descr};{i.BeginTime:yyyy-MM-dd HH:mm};{i.EndTime:yyyy-MM-dd HH:mm};{i.Lenght};{i.CommonProstoy};{i.Category};{i.Severity}");
        }

        // UTF-8 BOM для корректного отображения кириллицы в Excel
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();

        return File(bytes, "text/csv", $"report_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}