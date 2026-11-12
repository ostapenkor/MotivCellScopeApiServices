using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;

namespace MotivCellScopeApiServices.Controllers;

[ApiController, Authorize, Route("api/bts")]
public class BtsController(MotivReportDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery, Range(1, ushort.MaxValue)] ushort? regionId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? technology, CancellationToken ct)
    {
        if (from.HasValue != to.HasValue || (from.HasValue && from >= to))
            return BadRequest(new { error = "validation_failed", message = "provide both from and to, with from before to" });
        if (technology != null)
            return BadRequest(new { error = "unsupported_filter", message = "technology is not stored in the current database schema", field = "technology" });
        var stations = await db.Bts.AsNoTracking().Where(b => !regionId.HasValue || b.RegionId == regionId.Value)
            .OrderBy(b => b.Code).Select(b => new
            {
                b.BtsId, b.Code, b.RegionId, RegionCode = b.Region.Code, b.Lat, b.Lon,
                b.SiteType, b.Status, CellsCount = b.Cells.Count
            }).ToListAsync(ct);
        // Active and explained anomalies colour the map. A station's representative anomaly is
        // chosen deterministically by severity, time, then ID.
        var anomalies = await new ReportingQueries(db).Anomalies(from?.UtcDateTime, to?.UtcDateTime, regionId)
            .Where(a => a.Cell != null && (a.Status == "new" || a.Status == "acknowledged" || a.Status == "explained"))
            .Select(a => new { a.Cell!.BtsId, a.AnomalyId, a.Severity, a.TsFrom, a.KpiCode, a.ExplainedByIncident })
            .ToListAsync(ct);
        var grouped = anomalies.ToLookup(a => a.BtsId);
        return Ok(stations.Select(b =>
        {
            var list = grouped[b.BtsId].ToList();
            var worst = list.OrderByDescending(a => a.Severity == "critical" ? 3 : a.Severity == "warning" ? 2 : 1)
                .ThenByDescending(a => a.TsFrom).ThenByDescending(a => a.AnomalyId).FirstOrDefault();
            return new { b.BtsId, b.Code, b.RegionId, b.RegionCode, b.Lat, b.Lon, b.SiteType, b.Status,
                b.CellsCount, AnomalySeverity = worst?.Severity, AnomalyCount = list.Count,
                KpiCode = worst?.KpiCode, IncidentId = worst?.ExplainedByIncident };
        }));
    }
}
