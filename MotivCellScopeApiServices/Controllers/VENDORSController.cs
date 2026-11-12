using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Controllers;

[ApiController]
[Authorize]
[Route("api/vendors")]
public class VENDORSController(MotivReportDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorDto>>> Get(CancellationToken cancellationToken)
        => await db.Vendors.AsNoTracking().OrderBy(v => v.VendorId)
            .Select(v => new VendorDto(v.VendorId, v.Code, v.Name, v.RatSupported))
            .ToListAsync(cancellationToken);
}
