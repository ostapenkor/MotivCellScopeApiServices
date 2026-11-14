using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ReferenceController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        public ReferenceController(MotivReportDbContext db)
        {
            _db = db;
        }

        // GET /api/reference/all
        [HttpGet("all")]
        public IActionResult GetAllReferences()
        {
            // 1. Области (regions)
            var regions = _db.Regions
                .AsNoTracking()
                .OrderBy(r => r.RegionId)
                .Select(r => new
                {
                    regionId = r.RegionId,
                    code = r.Code,
                    name = r.Name
                })
                .ToList();

            // 2. Направления (directions)
            var directions = _db.Directions
                .AsNoTracking()
                .OrderBy(d => d.DirectionId)
                .Select(d => new
                {
                    directionId = d.DirectionId,
                    code = d.Code,
                    name = d.Name,
                    kind = d.Kind
                })
                .ToList();

            // 3. Техцентры (technical_centers)
            var technicalCenters = _db.TechnicalCenters
                .AsNoTracking()
                .OrderBy(tc => tc.TechCenterId)
                .Select(tc => new
                {
                    techCenterId = tc.TechCenterId,
                    directionId = tc.DirectionId,
                    code = tc.Code,
                    name = tc.Name
                })
                .ToList();

            // 4. Приоритеты (incident_priorities) — сортируем по sort_order
            var priorities = _db.IncidentPriorities
                .AsNoTracking()
                .OrderBy(p => p.SortOrder)
                .Select(p => new
                {
                    priorityId = p.PriorityId,
                    code = p.Code,
                    name = p.Name,
                    sortOrder = p.SortOrder
                })
                .ToList();

            // 5. Типы сетей (network_types)
            var networkTypes = _db.NetworkTypes
                .AsNoTracking()
                .OrderBy(nt => nt.NetworkTypeId)
                .Select(nt => new
                {
                    networkTypeId = nt.NetworkTypeId,
                    code = nt.Code,
                    name = nt.Name
                })
                .ToList();

            // 6. Типы оборудования (equipment_types)
            var equipmentTypes = _db.EquipmentTypes
                .AsNoTracking()
                .OrderBy(et => et.EquipmentTypeId)
                .Select(et => new
                {
                    equipmentTypeId = et.EquipmentTypeId,
                    code = et.Code,
                    name = et.Name
                })
                .ToList();

            // 7. Типы аварий (alarm_types)
            var alarmTypes = _db.AlarmTypes
                .AsNoTracking()
                .OrderBy(at => at.AlarmTypeId)
                .Select(at => new
                {
                    alarmTypeId = at.AlarmTypeId,
                    code = at.Code,
                    name = at.Name
                })
                .ToList();

            // 8. Статические справочники (ENUM из таблицы incidents)
            var categories = new[]
            {
                new { code = "power",     label = "Электроснабжение" },
                new { code = "transport", label = "Транспорт" },
                new { code = "hardware",  label = "Оборудование" },
                new { code = "software",  label = "ПО" },
                new { code = "planned",   label = "Плановые работы" },
                new { code = "client",    label = "Обращение клиента" },
                new { code = "external",  label = "Внешнее" },
                new { code = "other",     label = "Прочее" }
            };

            var severities = new[]
            {
                new { code = "low",      label = "Низкая" },
                new { code = "medium",   label = "Средняя" },
                new { code = "high",     label = "Высокая" },
                new { code = "critical", label = "Критическая" }
            };

            return Ok(new
            {
                regions = regions,
                directions = directions,
                technicalCenters = technicalCenters,
                priorities = priorities,
                networkTypes = networkTypes,
                equipmentTypes = equipmentTypes,
                alarmTypes = alarmTypes,
                categories = categories,
                severities = severities
            });
        }
    }
}
