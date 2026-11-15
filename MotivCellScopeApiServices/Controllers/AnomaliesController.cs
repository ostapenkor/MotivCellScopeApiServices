using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AnomaliesController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        

        public AnomaliesController(MotivReportDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        private readonly AuditService _audit;

        // =========================================================================
        // GET /api/monitoring/anomalies (и /api/anomalies)
        // Полный список аномалий со всеми фильтрами и привязкой к инцидентам
        // =========================================================================
        [HttpGet]
        public IActionResult GetAnomalies(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? severity,
            [FromQuery] string? status,
            [FromQuery] string? type,
            [FromQuery] string? kpiCode,
            [FromQuery] ushort? regionId,
            [FromQuery] uint? btsId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (!from.HasValue || !to.HasValue)
            {
                return BadRequest(new { message = "Параметры 'from' и 'to' обязательны и должны быть в формате ISO 8601." });
            }

            if (from > to)
            {
                return BadRequest(new { message = "Дата начала 'from' не может быть больше даты окончания 'to'." });
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var fromUtc = from.Value.ToUniversalTime();
            var toUtc = to.Value.ToUniversalTime();

            // Объединяем anomalies с cells, network_elements, regions и incidents (LEFT JOIN)
            var query = from a in _db.Anomalies.AsNoTracking()
                        join c in _db.Cells.AsNoTracking() on a.CellId equals c.CellId into cellGroup
                        from cell in cellGroup.DefaultIfEmpty()
                        join ne in _db.NetworkElements.AsNoTracking() on cell.BtsId equals ne.Id into neGroup
                        from netElem in neGroup.DefaultIfEmpty()
                        join r in _db.Regions.AsNoTracking() on netElem.RegionId equals r.RegionId into regGroup
                        from reg in regGroup.DefaultIfEmpty()
                        join inc in _db.Incidents.AsNoTracking() on a.ExplainedByIncident equals inc.IncidentId into incGroup
                        from incident in incGroup.DefaultIfEmpty()
                        where a.TsFrom >= fromUtc && a.TsFrom <= toUtc
                        select new
                        {
                            Anomaly = a,
                            Cell = cell,
                            NetElem = netElem,
                            Region = reg,
                            Incident = incident
                        };

            // Применяем опциональные фильтры
            if (!string.IsNullOrWhiteSpace(severity))
            {
                query = query.Where(x => x.Anomaly.Severity == severity);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Anomaly.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(x => x.Anomaly.AnomalyType == type);
            }

            if (!string.IsNullOrWhiteSpace(kpiCode))
            {
                query = query.Where(x => x.Anomaly.KpiCode == kpiCode);
            }

            if (regionId.HasValue)
            {
                query = query.Where(x => x.NetElem != null && x.NetElem.RegionId == regionId.Value);
            }

            if (btsId.HasValue)
            {
                query = query.Where(x => x.Cell != null && x.Cell.BtsId == btsId.Value);
            }

            // Считаем общее количество до пагинации
            int total = query.Count();

            // Сортировка от свежих к старым + пагинация
            var rawItems = query
                .OrderByDescending(x => x.Anomaly.TsFrom)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Маппинг в объединенный DTO (включает все поля из обоих вариантов)
            var items = rawItems.Select(x => new
            {
                anomalyId = x.Anomaly.AnomalyId,
                detectedAt = x.Anomaly.DetectedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                cellId = x.Anomaly.CellId,
                btsId = x.NetElem?.Id,
                btsCode = x.NetElem?.Code,
                btsFullName = x.NetElem?.FullName,
                sector = x.Cell?.Sector,
                regionId = x.Region?.RegionId,
                regionCode = x.Region?.Code,
                tsFrom = x.Anomaly.TsFrom.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                tsTo = x.Anomaly.TsTo.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                type = x.Anomaly.AnomalyType,
                kpiCode = x.Anomaly.KpiCode,
                severity = x.Anomaly.Severity,
                observedValue = x.Anomaly.ObservedValue,
                expectedValue = x.Anomaly.ExpectedValue,
                score = x.Anomaly.Score,
                status = x.Anomaly.Status,
                incidentId = x.Anomaly.ExplainedByIncident,
                incidentPw = x.Incident?.Pw, // Бонус для быстрого tooltip без доп. запроса
                incidentCategory = x.Incident?.Category
            }).ToList();

            return Ok(new
            {
                total = total,
                page = page,
                pageSize = pageSize,
                items = items
            });
        }

        // =========================================================================
        // GET /api/monitoring/anomalies/{id} (и /api/anomalies/{id})
        // Детальная карточка аномалии с вложенным объектом incident
        // =========================================================================
        [HttpGet("{id}")]
        public IActionResult GetAnomalyById(ulong id)
        {
            var item = (from a1 in _db.Anomalies.AsNoTracking()
                        join c in _db.Cells.AsNoTracking() on a1.CellId equals c.CellId into cellGroup
                        from cell in cellGroup.DefaultIfEmpty()
                        join ne in _db.NetworkElements.AsNoTracking() on cell.BtsId equals ne.Id into neGroup
                        from netElem in neGroup.DefaultIfEmpty()
                        join r in _db.Regions.AsNoTracking() on netElem.RegionId equals r.RegionId into regGroup
                        from reg in regGroup.DefaultIfEmpty()
                        join inc1 in _db.Incidents.AsNoTracking() on a1.ExplainedByIncident equals inc1.IncidentId into incGroup
                        from incident in incGroup.DefaultIfEmpty()
                        where a1.AnomalyId == id
                        select new
                        {
                            Anomaly = a1,
                            Cell = cell,
                            NetElem = netElem,
                            Region = reg,
                            Incident = incident
                        }).FirstOrDefault();

            if (item == null)
            {
                return NotFound(new { error = "not_found" });
            }

            var a = item.Anomaly;
            var inc = item.Incident;

            return Ok(new
            {
                anomalyId = a.AnomalyId,
                detectedAt = a.DetectedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                cellId = a.CellId,
                btsId = item.NetElem?.Id,
                btsCode = item.NetElem?.Code,
                btsFullName = item.NetElem?.FullName,
                sector = item.Cell?.Sector,
                regionId = item.Region?.RegionId,
                regionCode = item.Region?.Code,
                tsFrom = a.TsFrom.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                tsTo = a.TsTo.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                type = a.AnomalyType,
                kpiCode = a.KpiCode,
                severity = a.Severity,
                observedValue = a.ObservedValue,
                expectedValue = a.ExpectedValue,
                score = a.Score,
                status = a.Status,
                // Вложенный объект incident (будет null, если аномалия еще не объяснена)
                incident = inc == null ? null : new
                {
                    incidentId = inc.IncidentId,
                    pw = inc.Pw,
                    descr = inc.Descr,
                    category = inc.Category,
                    beginTime = inc.BeginTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            });
        }

        // =========================================================================
        // POST /api/anomalies/{id}/explain (и /api/monitoring/anomalies/{id}/explain)
        // Привязка аномалии к инциденту
        // =========================================================================
        [HttpPost("{id}/explain")]
        [Authorize(Roles = "operator,admin")]
        public IActionResult ExplainAnomaly(ulong id, [FromBody] ExplainAnomalyRequest request)
        {
            var anomaly = _db.Anomalies.FirstOrDefault(a => a.AnomalyId == id);
            if (anomaly == null)
            {
                return NotFound(new { error = "anomaly_not_found" });
            }

            bool incidentExists = _db.Incidents.Any(i => i.IncidentId == request.IncidentId);
            if (!incidentExists)
            {
                return BadRequest(new { error = "validation_failed", field = "incidentId", message = "Указанный инцидент не существует." });
            }
            anomaly.ExplainedByIncident = request.IncidentId;
            anomaly.Status = "explained";
            _db.SaveChanges();

            _audit.Log("explain_anomaly", "anomaly", id);
            return Ok(new
            {
                anomalyId = anomaly.AnomalyId,
                explainedByIncident = anomaly.ExplainedByIncident,
                status = anomaly.Status
            });
        }

        // =========================================================================
        // PATCH /api/anomalies/{id}/status (и /api/monitoring/anomalies/{id}/status)
        // Ручная смена статуса аномалии
        // =========================================================================
        [HttpPatch("{id}/status")]
        [Authorize(Roles = "operator,admin")]
        public IActionResult UpdateAnomalyStatus(ulong id, [FromBody] UpdateAnomalyStatusRequest request)
        {
            var allowedStatuses = new[] { "new", "acknowledged", "explained", "resolved", "false_positive" };
            var newStatus = request.Status?.Trim().ToLower();
            if (string.IsNullOrEmpty(newStatus) || !allowedStatuses.Contains(newStatus))
            {
                return BadRequest(new { error = "validation_failed", field = "status" });
            }
            var anomaly = _db.Anomalies.FirstOrDefault(a => a.AnomalyId == id);
            if (anomaly == null)
            {
                return NotFound(new { error = "not_found" });
            }
            anomaly.Status = newStatus;
            if (newStatus == "new" || newStatus == "false_positive")
            {
                anomaly.ExplainedByIncident = null;
            }
            _db.SaveChanges();

            _audit.Log("update_anomaly_status", "anomaly", id);
            return Ok(new
            {
                anomalyId = anomaly.AnomalyId,
                status = anomaly.Status,
                explainedByIncident = anomaly.ExplainedByIncident
            });
        }
    }
}


public class ExplainAnomalyRequest
{
    public ulong IncidentId { get; set; }
}
public class UpdateAnomalyStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

