using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IncidentsController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        public IncidentsController(MotivReportDbContext db)
        {
            _db = db;
        }

        // GET /api/incidents?from=2026-10-12T00:00:00Z&to=2026-11-11T00:00:00Z
        [HttpGet]
        [Authorize]
        public IActionResult GetIncidents([FromQuery] DateTime? from,[FromQuery] DateTime? to)
        {
            // 1. Валидация обязательных параметров периода
            if (!from.HasValue || !to.HasValue)
            {
                return BadRequest(new { message = "Параметры 'from' и 'to' обязательны и должны быть в формате ISO 8601." });
            }

            if (from > to)
            {
                return BadRequest(new { message = "Дата начала 'from' не может быть больше даты окончания 'to'." });
            }

            var fromUtc = from.Value.ToUniversalTime();
            var toUtc = to.Value.ToUniversalTime();

            // 2. Запрос к представлению v_incidents_full с подтягиванием имени автора из users
            var query = from v in _db.VIncidentsFulls.AsNoTracking()
                        join u in _db.Users.AsNoTracking() on v.CreatedByUserId equals u.UserId into userGroup
                        from user in userGroup.DefaultIfEmpty()
                        where v.BeginTime >= fromUtc && v.BeginTime <= toUtc
                        orderby v.BeginTime descending
                        select new
                        {
                            View = v,
                            CreatedByUsername = user != null ? user.Username : null
                        };

            // 3. Выполняем запрос к базе
            var rawItems = query.ToList();

            // 4. Формируем JSON-ответ строго по контракту
            var items = rawItems.Select(x => new
            {
                incidentId = x.View.IncidentId,
                pw = x.View.Pw,
                directionCode = x.View.DirectionCode,
                techCenterCode = x.View.TechCenterCode,
                priorityCode = x.View.PriorityCode,
                networkTypeCode = x.View.NetworkTypeCode,
                equipmentTypeCode = x.View.EquipmentTypeCode,
                alarmTypeCode = x.View.AlarmTypeCode,
                networkElementType = x.View.NetworkElementType,
                networkElementName = x.View.NetworkElementName,
                servElem = x.View.ServElem,
                numbRbs = x.View.NumbRbs,
                descr = x.View.Descr,
                beginTime = x.View.BeginTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = x.View.EndTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                akb = x.View.Akb?.ToString(),
                dgu = x.View.Dgu?.ToString(),
                lenght = x.View.Lenght,
                prostoy = x.View.Prostoy?.ToString(),
                commonProstoy = x.View.CommonProstoy,
                alarmWho = x.View.AlarmWho,
                descNotice = x.View.DescNotice,
                adminDistrict = x.View.AdminDistrict,
                severity = x.View.Severity,
                category = x.View.Category,
                createdBy = x.CreatedByUsername,
                createdAt = x.View.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }).ToList();

            return Ok(new
            {
                total = items.Count,
                items = items
            });
        }

    }
}
