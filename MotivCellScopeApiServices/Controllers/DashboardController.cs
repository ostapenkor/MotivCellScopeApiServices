using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;

namespace MotivCellScopeApiServices.Controllers;

[ApiController, Authorize, Route("api/dashboard")]
public class DashboardController(MotivReportDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] PeriodQuery period, CancellationToken ct)
    {
        var queries = new ReportingQueries(db);
        var totals = await queries.Kpis(period).GroupBy(k => 1).Select(g => new
        {
            Attempts = g.Sum(k => (decimal)k.ConnAttempt),
            Successes = g.Sum(k => (decimal)k.ConnSuccess),
            Drops = g.Sum(k => (decimal)k.ConnDrop),
            Traffic = g.Sum(k => (decimal)k.TrafficDlBytes + (decimal)k.TrafficUlBytes),
            Hours = g.Count(),
            Available = g.Count(k => k.ConnAttempt > 0 || k.TrafficDlBytes > 0 || k.TrafficUlBytes > 0)
        }).SingleOrDefaultAsync(ct);
        var stations = db.Bts.AsNoTracking().Where(b => !period.RegionId.HasValue || b.RegionId == period.RegionId.Value);
        var btsTotal = await stations.CountAsync(ct);
        var btsActive = await stations.CountAsync(b => b.Status == "active", ct);
        var btsProblem = await stations.CountAsync(b => b.Status == "degraded" || b.Status == "down", ct);
        var cellsTotal = await db.Cells.CountAsync(c => !period.RegionId.HasValue || c.Bts.RegionId == period.RegionId.Value, ct);
        var anomalies = queries.Anomalies(period.From!.Value.UtcDateTime, period.To!.Value.UtcDateTime, period.RegionId);
        var open = anomalies.Where(a => a.Status == "new" || a.Status == "acknowledged");
        return Ok(new
        {
            Period = new { From = period.From.Value.UtcDateTime, To = period.To.Value.UtcDateTime },
            AvgCssr = totals == null || totals.Attempts == 0 ? (decimal?)null : 100m * totals.Successes / totals.Attempts,
            AvgDcr = totals == null || totals.Successes == 0 ? (decimal?)null : 100m * totals.Drops / totals.Successes,
            AvgAvailability = totals == null ? (decimal?)null : 100m * totals.Available / totals.Hours,
            TotalTrafficGb = (totals?.Traffic ?? 0) / ReportingQueries.BytesPerGb,
            BtsTotal = btsTotal, BtsActive = btsActive, BtsProblem = btsProblem, CellsTotal = cellsTotal,
            AnomaliesOpen = await open.CountAsync(ct),
            AnomaliesExplained = await anomalies.CountAsync(a => a.Status == "explained", ct),
            AnomaliesCritical = await open.CountAsync(a => a.Severity == "critical", ct)
        });
    }
}
