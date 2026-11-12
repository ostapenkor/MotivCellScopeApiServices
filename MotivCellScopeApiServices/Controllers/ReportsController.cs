using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;

namespace MotivCellScopeApiServices.Controllers;

[ApiController, Authorize, Route("api/reports")]
public class ReportsController(MotivReportDbContext db) : ControllerBase
{
    [HttpGet("export")]
    [Produces("text/csv", "application/json")]
    public async Task<IActionResult> Export([FromQuery] PeriodQuery period,
        [FromQuery] string format = "csv", CancellationToken ct = default)
    {
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "validation_failed", message = "only csv format is supported", field = "format" });
        var rows = await new ReportingQueries(db).CellReport(period, ct);
        var csv = new StringBuilder("cell_id,bts_code,sector,region,cssr,dcr,availability,traffic_gb,anomalies\r\n");
        foreach (var r in rows)
            csv.AppendJoin(",", r.CellId.ToString(CultureInfo.InvariantCulture), Escape(r.BtsCode), Escape(r.Sector),
                Escape(r.Region), Number(r.Cssr), Number(r.Dcr), Number(r.Availability), Number(r.TrafficGb),
                r.Anomalies.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        // UTF-8 BOM lets Excel recognise Cyrillic text. Escape spreadsheet formulas as text.
        var encoding = new UTF8Encoding(true);
        return File(encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"report_{period.From!.Value.UtcDateTime:yyyy-MM-dd}_{period.To!.Value.UtcDateTime:yyyy-MM-dd}.csv");
    }

    private static string Number(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
    private static string Escape(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
