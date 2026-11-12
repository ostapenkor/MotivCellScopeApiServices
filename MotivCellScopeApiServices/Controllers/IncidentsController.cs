using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Controllers;

[ApiController, Authorize, Route("api/incidents")]
public class IncidentsController(MotivReportDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] IncidentsQuery query, CancellationToken ct)
    {
        var from = query.From!.Value.UtcDateTime;
        var to = query.To!.Value.UtcDateTime;
        var items = db.Incidents.AsNoTracking().Where(i => i.StartedAt < to && (i.ResolvedAt == null || i.ResolvedAt > from))
            .Where(i => !query.RegionId.HasValue || i.RegionId == query.RegionId.Value)
            .Where(i => query.Category == null || i.Category == query.Category)
            .Where(i => query.Severity == null || i.Severity == query.Severity);
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue) return BadRequest(new { error = "validation_failed", message = "page is too large" });
        var total = await items.CountAsync(ct);
        var rows = await items.OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.IncidentId)
            .Skip((int)offset).Take(query.PageSize).Select(i => new
            {
                i.IncidentId, BtsCode = i.Bts == null ? null : i.Bts.Code, RegionCode = i.Region.Code,
                i.Category, i.Severity, i.StartedAt, i.ResolvedAt, i.Description,
                AnomaliesCount = i.Anomalies.Count(a => a.Status != "false_positive")
            }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var result = rows.Select(i => new
        {
            i.IncidentId, i.BtsCode, i.RegionCode, i.Category, i.Severity,
            StartedAt = DateTime.SpecifyKind(i.StartedAt, DateTimeKind.Utc),
            ResolvedAt = i.ResolvedAt.HasValue ? DateTime.SpecifyKind(i.ResolvedAt.Value, DateTimeKind.Utc) : (DateTime?)null,
            DurationMin = Math.Max(0, (i.ResolvedAt ?? now).Subtract(i.StartedAt).TotalMinutes),
            i.Description, i.AnomaliesCount
        }).ToList();
        return Ok(new { Total = total, query.Page, query.PageSize, Items = result });
    }
}
