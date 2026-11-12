using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Services;

public class ReportingQueries(MotivReportDbContext db)
{
    public const decimal BytesPerGb = 1_000_000_000m;

    public IQueryable<KpiCellHourly> Kpis(PeriodQuery period)
    {
        var from = period.From!.Value.UtcDateTime;
        var to = period.To!.Value.UtcDateTime;
        return db.KpiCellHourlies.AsNoTracking().Where(k => k.TsHour >= from && k.TsHour < to)
            .Where(k => !period.RegionId.HasValue || k.Cell.Bts.RegionId == period.RegionId.Value);
    }

    public IQueryable<Anomaly> Anomalies(DateTime? from, DateTime? to, ushort? regionId)
        => db.Anomalies.AsNoTracking()
            .Where(a => (!from.HasValue || a.TsTo > from.Value)
                && (!to.HasValue || a.TsFrom < to.Value))
            .Where(a => !regionId.HasValue || (a.Cell != null && a.Cell.Bts.RegionId == regionId.Value));

    public async Task<List<CellReportDto>> CellReport(PeriodQuery period, CancellationToken ct)
    {
        // Aggregate in SQL; cells without measurements still appear with null KPI values.
        var totals = await Kpis(period).GroupBy(k => k.CellId).Select(g => new
        {
            CellId = g.Key,
            Attempts = g.Sum(k => (decimal)k.ConnAttempt),
            Successes = g.Sum(k => (decimal)k.ConnSuccess),
            Drops = g.Sum(k => (decimal)k.ConnDrop),
            Traffic = g.Sum(k => (decimal)k.TrafficDlBytes + (decimal)k.TrafficUlBytes),
            Hours = g.Count(),
            Available = g.Count(k => k.ConnAttempt > 0 || k.TrafficDlBytes > 0 || k.TrafficUlBytes > 0)
        }).ToDictionaryAsync(t => t.CellId, ct);
        var anomalyCounts = await Anomalies(period.From!.Value.UtcDateTime, period.To!.Value.UtcDateTime,
                period.RegionId).Where(a => a.CellId != null && a.Status != "false_positive")
            .GroupBy(a => a.CellId).Select(g => new { CellId = g.Key!.Value, Count = g.Count() })
            .ToDictionaryAsync(a => a.CellId, a => a.Count, ct);
        var cells = await db.Cells.AsNoTracking()
            .Where(c => !period.RegionId.HasValue || c.Bts.RegionId == period.RegionId.Value)
            .OrderBy(c => c.Bts.Code).ThenBy(c => c.LocalCellId)
            .Select(c => new { c.CellId, BtsCode = c.Bts.Code, c.Sector, Region = c.Bts.Region.Code })
            .ToListAsync(ct);
        return cells.Select(c =>
        {
            totals.TryGetValue(c.CellId, out var t);
            return new CellReportDto(c.CellId, c.BtsCode, c.Sector, c.Region,
                t == null || t.Attempts == 0 ? null : 100m * t.Successes / t.Attempts,
                t == null || t.Successes == 0 ? null : 100m * t.Drops / t.Successes,
                t == null ? null : 100m * t.Available / t.Hours,
                t == null ? 0m : t.Traffic / BytesPerGb, anomalyCounts.GetValueOrDefault(c.CellId));
        }).ToList();
    }
}
