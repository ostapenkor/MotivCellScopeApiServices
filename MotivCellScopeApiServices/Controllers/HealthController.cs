using Microsoft.AspNetCore.Mvc;
using MotivCellScopeApiServices.Models;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        public HealthController(MotivReportDbContext db)
        {
            _db = db;
        }

        // GET /api/health
        [HttpGet]
        public IActionResult GetHealth()
        {
            bool isDbConnected = false;

            try
            {
                // Синхронная проверка физического соединения с MariaDB
                isDbConnected = _db.Database.CanConnect();
            }
            catch
            {
                isDbConnected = false;
            }

            // Если база недоступна — возвращаем 503 строго по контракту из ТЗ
            if (!isDbConnected)
            {
                return StatusCode(503, new
                {
                    status = "degraded",
                    database = "unavailable"
                });
            }

            // Если всё в порядке — возвращаем 200 OK
            return Ok(new
            {
                status = "ok",
                database = "ok",
                version = "1.0.0",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }
    }
}
