using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using System.Diagnostics;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Задает базовый путь /api/admin
    [Authorize(Roles = "admin")] // Пересчёты разрешены только администратору.
    public class AdminController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        public AdminController(MotivReportDbContext db)
        {
            _db = db;
        }

        // =========================================================================
        // 1. POST /api/admin/refresh-kpi
        // =========================================================================
        [HttpPost("refresh-kpi")]
        public IActionResult RefreshKpi([FromBody] PeriodRequest request)
        {
            var sw = Stopwatch.StartNew();

            var fromStr = request.From.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var toStr = request.To.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");

            // Вызываем хранимую процедуру. ExecuteSqlRaw возвращает количество затронутых строк
            
            _db.Database.ExecuteSqlRaw(
                "CALL sp_refresh_kpi_hourly({0}, {1});", fromStr, toStr);

            // Если процедура внутри себя не возвращает affectedRows (возвращает 0 или -1), 
            // страхуемся и считаем реальное количество строк за этот период
            int affectedRows = _db.KpiCellHourlies
                    .Count(k => k.TsHour >= request.From.ToUniversalTime() && k.TsHour <= request.To.ToUniversalTime());

            
            sw.Stop();
            return Ok(new
            {
                processed = affectedRows,
                durationMs = sw.ElapsedMilliseconds
            });
        }

        // =========================================================================
        // 2. POST /api/admin/detect-anomalies
        // =========================================================================
        [HttpPost("detect-anomalies")]
        public IActionResult DetectAnomalies([FromBody] DetectAnomaliesRequest request)
        {
            var sw = Stopwatch.StartNew();

            // Запоминаем количество аномалий ДО запуска процедуры
            int countBefore = _db.Anomalies.Count();

            // Вызываем процедуру поиска аномалий
            int affectedRows = _db.Database.ExecuteSqlRaw(
                "CALL sp_detect_anomalies({0});", request.HoursBack);

            // Считаем сколько стало ПОСЛЕ
            int countAfter = _db.Anomalies.Count();
            int newlyFound = Math.Max(0, countAfter - countBefore);
            int processedCount = affectedRows > 0 ? affectedRows : newlyFound;

            
            sw.Stop();
            return Ok(new
            {
                found = newlyFound,
                processed = processedCount,
                durationMs = sw.ElapsedMilliseconds
            });
        }

        // =========================================================================
        // 3. POST /api/admin/detect-availability
        // =========================================================================
        [HttpPost("detect-availability")]
        public IActionResult DetectAvailability([FromBody] PeriodRequest request)
        {
            var sw = Stopwatch.StartNew();

            var fromStr = request.From.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var toStr = request.To.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");

            int countBefore = _db.Anomalies.Count();

            _db.Database.ExecuteSqlRaw(
                "CALL sp_detect_availability_anomalies({0}, {1});", fromStr, toStr);

            int countAfter = _db.Anomalies.Count();
            int newlyFound = Math.Max(0, countAfter - countBefore);

            
            sw.Stop();
            return Ok(new
            {
                found = newlyFound,
                durationMs = sw.ElapsedMilliseconds
            });
        }

        // =========================================================================
        // 4. POST /api/admin/recalculate-all
        // =========================================================================
        [HttpPost("recalculate-all")]
        public IActionResult RecalculateAll([FromBody] PeriodRequest request)
        {
            var totalSw = Stopwatch.StartNew();

            var fromUtc = request.From.ToUniversalTime();
            var toUtc = request.To.ToUniversalTime();
            var fromStr = fromUtc.ToString("yyyy-MM-dd HH:mm:ss");
            var toStr = toUtc.ToString("yyyy-MM-dd HH:mm:ss");

            // 1. Считаем разницу в часах для процедуры sp_detect_anomalies (по умолчанию 720)
            int hoursBack = (int)Math.Ceiling((toUtc - fromUtc).TotalHours);
            if (hoursBack <= 0) hoursBack = 720;

            // Шаг А: Пересчет KPI
            int kpiRows = _db.Database.ExecuteSqlRaw(
                "CALL sp_refresh_kpi_hourly({0}, {1});", fromStr, toStr);
            if (kpiRows <= 0)
            {
                kpiRows = _db.KpiCellHourlies.Count(k => k.TsHour >= fromUtc && k.TsHour <= toUtc);
            }

            // Шаг Б: Поиск основных аномалий
            int anomaliesBefore = _db.Anomalies.Count();
            _db.Database.ExecuteSqlRaw("CALL sp_detect_anomalies({0});", hoursBack);
            int anomaliesAfterMain = _db.Anomalies.Count();
            int mainAnomaliesFound = Math.Max(0, anomaliesAfterMain - anomaliesBefore);

            // Шаг В: Поиск аномалий доступности
            _db.Database.ExecuteSqlRaw(
                "CALL sp_detect_availability_anomalies({0}, {1});", fromStr, toStr);
            int anomaliesAfterAvail = _db.Anomalies.Count();
            int availAnomaliesFound = Math.Max(0, anomaliesAfterAvail - anomaliesAfterMain);

            
            totalSw.Stop();
            return Ok(new
            {
                kpiRefreshed = kpiRows,
                anomaliesFound = mainAnomaliesFound,
                availabilityFound = availAnomaliesFound,
                durationMs = totalSw.ElapsedMilliseconds
            });
        }

    }

    // Модели входящих JSON-запросов
    public class PeriodRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }

    public class DetectAnomaliesRequest
    {
        public int HoursBack { get; set; } = 720;
    }
}
