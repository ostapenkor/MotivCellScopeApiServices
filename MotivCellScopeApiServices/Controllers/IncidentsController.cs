using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using System.Text;

namespace MotivCellScopeApiServices.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IncidentsController : ControllerBase
{
    private readonly MotivReportDbContext _db;

    public IncidentsController(MotivReportDbContext db)
    {
        _db = db;
    }

    // 1. Получение списка (синхронное)
    [HttpGet]
    public IActionResult GetList(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.Incidents.AsQueryable();

        if (from.HasValue) query = query.Where(i => i.BeginTime >= from.Value);
        if (to.HasValue) query = query.Where(i => i.BeginTime <= to.Value);

        var total = query.Count();

        var items = query
            .OrderByDescending(i => i.BeginTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new {
                i.IncidentId,
                i.Pw,
                i.BeginTime,
                i.EndTime,
                i.Lenght,
                i.CommonProstoy
            })
            .ToList(); // Синхронный вызов

        return Ok(new { total, page, pageSize, items });
    }

    // 2. Получение по ID
    [HttpGet("{id}")]
    public IActionResult GetById(long id)
    {
        var incident = _db.Incidents.Find(id); // Синхронный метод
        if (incident == null) return NotFound(new { error = "Инцидент не найден" });
        return Ok(incident);
    }

    // 3. Создание
    [HttpPost]
    [Authorize(Roles = "operator,admin")]
    public IActionResult Create([FromBody] Incident incident)
    {
        if (incident.BeginTime == default)
            return BadRequest(new { error = "Укажите время начала" });

        _db.Incidents.Add(incident);
        _db.SaveChanges(); // Синхронное сохранение

        return CreatedAtAction(nameof(GetById), new { id = incident.IncidentId }, incident);
    }

    // 4. Экспорт в CSV
    [HttpGet("export")]
    public IActionResult ExportCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Incidents.AsQueryable();
        if (from.HasValue) query = query.Where(i => i.BeginTime >= from.Value);
        if (to.HasValue) query = query.Where(i => i.BeginTime <= to.Value);

        // Важно: добавили .ToList() в конце, чтобы получить список, а не Task
        var list = query.OrderByDescending(i => i.BeginTime).ToList();

        var csv = new StringBuilder();
        csv.AppendLine("ID;PW;Начало;Окончание;Длительность");

        foreach (var i in list)
        {
            csv.AppendLine($"{i.IncidentId};{i.Pw};{i.BeginTime:yyyy-MM-dd HH:mm};{i.EndTime:yyyy-MM-dd HH:mm};{i.Lenght}");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"incidents_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}