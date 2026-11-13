using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Controllers;
[ApiController, Authorize, Route("api/regions")]
public class RegionsController : ControllerBase
{
    private readonly MotivReportDbContext db;
    public RegionsController(MotivReportDbContext db) => this.db = db;
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Regions.AsNoTracking().OrderBy(r => r.RegionId).Select(r => new { r.RegionId, r.Code, r.Name, r.TzOffsetMin }).ToListAsync(ct));
}
