using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;
using System.Security.Claims;
using System.Text;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class IncidentsController : ControllerBase
    {
        private readonly MotivReportDbContext _db;
        private readonly AuditService _audit;
        public IncidentsController(MotivReportDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        // GET /api/incidents?from=2026-10-12T00:00:00Z&to=2026-11-11T00:00:00Z
        [HttpGet]
        
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

        // GET /api/incidents/{id}
        [HttpGet("{id}")]
        public IActionResult GetIncidentById(ulong id)
        {
            // Объединяем таблицу incidents (для получения всех *Id и updatedAt) 
            // с представлением v_incidents_full (для получения *Code и networkElementName)
            var item = (from i in _db.Incidents.AsNoTracking()
                        join v1 in _db.VIncidentsFulls.AsNoTracking() on i.IncidentId equals v1.IncidentId
                        where i.IncidentId == id
                        select new
                        {
                            Entity = i,
                            View = v1
                        }).FirstOrDefault();

            // Если инцидент не найден — возвращаем 404 строго по контракту
            if (item == null)
            {
                return NotFound(new { error = "not_found" });
            }

            var e = item.Entity;
            var v = item.View;

            // Формируем JSON-ответ со всеми ID, кодами и полями
            return Ok(new
            {
                incidentId = e.IncidentId,
                pw = e.Pw,
                directionId = e.DirectionId,
                directionCode = v.DirectionCode,
                techCenterId = e.TechCenterId,
                techCenterCode = v.TechCenterCode,
                priorityId = e.PriorityId,
                priorityCode = v.PriorityCode,
                networkTypeId = e.NetworkTypeId,
                networkTypeCode = v.NetworkTypeCode,
                equipmentTypeId = e.EquipmentTypeId,
                equipmentTypeCode = v.EquipmentTypeCode,
                alarmTypeId = e.AlarmTypeId,
                alarmTypeCode = v.AlarmTypeCode,
                networkElementId = e.NetworkElementId,
                networkElementName = v.NetworkElementName,
                networkElementType = v.NetworkElementType,
                servElem = e.ServElem,
                numbRbs = e.NumbRbs,
                descr = e.Descr,
                beginTime = e.BeginTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = e.EndTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                akb = FormatTimeSpan(e.Akb),
                dgu = FormatTimeSpan(e.Dgu),
                lenght = e.Lenght,
                prostoy = FormatTimeSpan(e.Prostoy),
                commonProstoy = e.CommonProstoy,
                alarmWho = e.AlarmWho,
                descNotice = e.DescNotice,
                adminDistrict = e.AdminDistrict,
                regionId = e.RegionId,
                severity = e.Severity,
                category = e.Category,
                createdByUserId = e.CreatedByUserId,
                createdAt = e.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                updatedAt = e.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }

        
        // =========================================================================
        // 2.3. POST /api/incidents
        // =========================================================================
        [HttpPost]
        [Authorize(Roles = "operator,admin")] // Auth: только operator или admin
        public IActionResult CreateIncident([FromBody] CreateIncidentRequest request)
        {
            // 1. Валидация полей
            if (request.Pw != null && request.Pw.Length > 450)
            {
                return BadRequest(new { error = "validation_failed", field = "pw" });
            }

            var beginUtc = request.BeginTime.ToUniversalTime();
            // Даем небольшой запас в 1 минуту на возможную рассинхронизацию часов клиента и сервера
            if (beginUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return BadRequest(new { error = "validation_failed", field = "beginTime" });
            }

            DateTime? endUtc = request.EndTime?.ToUniversalTime();
            if (endUtc.HasValue && endUtc.Value < beginUtc)
            {
                return BadRequest(new { error = "validation_failed", field = "endTime" });
            }

            var allowedCategories = new[] { "power", "transport", "hardware", "software", "planned", "client", "external", "other" };
            var category = string.IsNullOrWhiteSpace(request.Category) ? "other" : request.Category.ToLower();
            if (!allowedCategories.Contains(category))
            {
                return BadRequest(new { error = "validation_failed", field = "category" });
            }

            var allowedSeverities = new[] { "low", "medium", "high", "critical" };
            var severity = string.IsNullOrWhiteSpace(request.Severity) ? "medium" : request.Severity.ToLower();
            if (!allowedSeverities.Contains(severity))
            {
                return BadRequest(new { error = "validation_failed", field = "severity" });
            }

            // Парсим строковые интервалы времени ("01:23:45") в TimeSpan
            TimeSpan? akbSpan = ParseTimeSpan(request.Akb);
            if (request.Akb != null && !akbSpan.HasValue)
                return BadRequest(new { error = "validation_failed", field = "akb" });

            TimeSpan? dguSpan = ParseTimeSpan(request.Dgu);
            if (request.Dgu != null && !dguSpan.HasValue)
                return BadRequest(new { error = "validation_failed", field = "dgu" });

            TimeSpan? prostoySpan = ParseTimeSpan(request.Prostoy);
            if (request.Prostoy != null && !prostoySpan.HasValue)
                return BadRequest(new { error = "validation_failed", field = "prostoy" });

            // 2. Если задан networkElementId — перезаписываем servElem значением full_name из БД
            string? finalServElem = request.ServElem;
            if (request.NetworkElementId.HasValue)
            {
                var netElement = _db.NetworkElements
                    .AsNoTracking()
                    .FirstOrDefault(ne => ne.Id == request.NetworkElementId.Value);

                if (netElement == null)
                {
                    return BadRequest(new { error = "validation_failed", field = "networkElementId" });
                }

                if (!string.IsNullOrEmpty(netElement.FullName))
                {
                    finalServElem = netElement.FullName;
                }
            }

            // 3. Вычисляем lenght и commonProstoy в формате «X days, HH:MM:SS»
            // Если endTime не указан (авария еще идет), считаем длительность до текущего момента
            var effectiveEnd = endUtc ?? DateTime.UtcNow;
            var duration = effectiveEnd - beginUtc;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            string formattedDuration = $"{duration.Days} days, {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";

            // Достаем ID и данные текущего пользователя из JWT-токена
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            uint? currentUserId = uint.TryParse(userIdStr, out uint uid) ? uid : null;
            var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var currentRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // 4. Создаем и сохраняем сущность Incident
            var newIncident = new Incident
            {
                Pw = request.Pw,
                DirectionId = request.DirectionId,
                TechCenterId = request.TechCenterId,
                PriorityId = request.PriorityId,
                NetworkTypeId = request.NetworkTypeId,
                NetworkElementId = request.NetworkElementId,
                ServElem = finalServElem,
                NumbRbs = request.NumbRbs,
                EquipmentTypeId = request.EquipmentTypeId,
                AlarmTypeId = request.AlarmTypeId,
                Descr = request.Descr,
                BeginTime = beginUtc,
                EndTime = endUtc,
                Akb = akbSpan,
                Dgu = dguSpan,
                Lenght = formattedDuration,
                Prostoy = prostoySpan,
                CommonProstoy = formattedDuration,
                AlarmWho = request.AlarmWho,
                DescNotice = request.DescNotice,
                AdminDistrict = request.AdminDistrict,
                RegionId = request.RegionId,
                Severity = severity,
                Category = category,
                CreatedByUserId = currentUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Incidents.Add(newIncident);
            _db.SaveChanges(); // После SaveChanges в newIncident.IncidentId появится сгенерированный ID

            // 5. Записываем событие в audit_log
            _audit.Log("create_incident", "incident", newIncident.IncidentId);

            // 6. Получаем полный DTO через представление v_incidents_full (как в GET /{id})
            var createdItem = (from i in _db.Incidents.AsNoTracking()
                               join v1 in _db.VIncidentsFulls.AsNoTracking() on i.IncidentId equals v1.IncidentId
                               where i.IncidentId == newIncident.IncidentId
                               select new { Entity = i, View = v1 }).First();

            var e = createdItem.Entity;
            var v = createdItem.View;

            var responseDto = new
            {
                incidentId = e.IncidentId,
                pw = e.Pw,
                directionId = e.DirectionId,
                directionCode = v.DirectionCode,
                techCenterId = e.TechCenterId,
                techCenterCode = v.TechCenterCode,
                priorityId = e.PriorityId,
                priorityCode = v.PriorityCode,
                networkTypeId = e.NetworkTypeId,
                networkTypeCode = v.NetworkTypeCode,
                equipmentTypeId = e.EquipmentTypeId,
                equipmentTypeCode = v.EquipmentTypeCode,
                alarmTypeId = e.AlarmTypeId,
                alarmTypeCode = v.AlarmTypeCode,
                networkElementId = e.NetworkElementId,
                networkElementName = v.NetworkElementName,
                networkElementType = v.NetworkElementType,
                servElem = e.ServElem,
                numbRbs = e.NumbRbs,
                descr = e.Descr,
                beginTime = e.BeginTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = e.EndTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                akb = FormatTimeSpan(e.Akb),
                dgu = FormatTimeSpan(e.Dgu),
                lenght = e.Lenght,
                prostoy = FormatTimeSpan(e.Prostoy),
                commonProstoy = e.CommonProstoy,
                alarmWho = e.AlarmWho,
                descNotice = e.DescNotice,
                adminDistrict = e.AdminDistrict,
                regionId = e.RegionId,
                severity = e.Severity,
                category = e.Category,
                createdByUserId = e.CreatedByUserId,
                createdAt = e.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                updatedAt = e.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            // Возвращаем 201 Created со ссылкой на GET /{id} и полным объектом в теле
            return CreatedAtAction(nameof(GetIncidentById), new { id = e.IncidentId }, responseDto);
        }

        // =========================================================================
        // 2.4. PUT /api/incidents/{id}
        // =========================================================================
        [HttpPut("{id}")]
        [Authorize(Roles = "operator,admin")] // Auth: только operator или admin (engineer получит 403 Forbidden)
        public IActionResult UpdateIncident(ulong id, [FromBody] CreateIncidentRequest request)
        {
            // 1. Проверяем существование инцидента в базе
            var existingIncident = _db.Incidents.FirstOrDefault(i => i.IncidentId == id);
            if (existingIncident == null)
            {
                return NotFound(new { error = "not_found" });
            }

            // 2. Валидация полей (аналогично POST)
            if (request.Pw != null && request.Pw.Length > 450)
            {
                return BadRequest(new { error = "validation_failed", field = "pw" });
            }

            var beginUtc = request.BeginTime.ToUniversalTime();
            if (beginUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return BadRequest(new { error = "validation_failed", field = "beginTime" });
            }

            DateTime? endUtc = request.EndTime?.ToUniversalTime();
            if (endUtc.HasValue && endUtc.Value < beginUtc)
            {
                return BadRequest(new { error = "validation_failed", field = "endTime" });
            }

            var allowedCategories = new[] { "power", "transport", "hardware", "software", "planned", "client", "external", "other" };
            var category = string.IsNullOrWhiteSpace(request.Category) ? "other" : request.Category.ToLower();
            if (!allowedCategories.Contains(category))
            {
                return BadRequest(new { error = "validation_failed", field = "category" });
            }

            var allowedSeverities = new[] { "low", "medium", "high", "critical" };
            var severity = string.IsNullOrWhiteSpace(request.Severity) ? "medium" : request.Severity.ToLower();
            if (!allowedSeverities.Contains(severity))
            {
                return BadRequest(new { error = "validation_failed", field = "severity" });
            }

            TimeSpan? akbSpan = ParseTimeSpan(request.Akb);
            if (request.Akb != null && !akbSpan.HasValue)
                return BadRequest(new { error = "validation_failed", field = "akb" });

            TimeSpan? dguSpan = ParseTimeSpan(request.Dgu);
            if (request.Dgu != null && !dguSpan.HasValue)
                return BadRequest(new { error = "validation_failed", field = "dgu" });

            TimeSpan? prostoySpan = ParseTimeSpan(request.Prostoy);
            if (request.Prostoy != null && !prostoySpan.HasValue)
                return BadRequest(new { error = "validation_failed", field = "prostoy" });

            // 3. Если задан networkElementId — подтягиваем актуальное full_name из network_elements
            string? finalServElem = request.ServElem;
            if (request.NetworkElementId.HasValue)
            {
                var netElement = _db.NetworkElements
                    .AsNoTracking()
                    .FirstOrDefault(ne => ne.Id == request.NetworkElementId.Value);

                if (netElement == null)
                {
                    return BadRequest(new { error = "validation_failed", field = "networkElementId" });
                }

                if (!string.IsNullOrEmpty(netElement.FullName))
                {
                    finalServElem = netElement.FullName;
                }
            }

            // 4. Пересчитываем lenght и commonProstoy в формате «X days, HH:MM:SS»
            var effectiveEnd = endUtc ?? DateTime.UtcNow;
            var duration = effectiveEnd - beginUtc;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            string formattedDuration = $"{duration.Days} days, {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";

            // 5. Обновляем поля сущности (CreatedByUserId и CreatedAt не трогаем)
            existingIncident.Pw = request.Pw;
            existingIncident.DirectionId = request.DirectionId;
            existingIncident.TechCenterId = request.TechCenterId;
            existingIncident.PriorityId = request.PriorityId;
            existingIncident.NetworkTypeId = request.NetworkTypeId;
            existingIncident.NetworkElementId = request.NetworkElementId;
            existingIncident.ServElem = finalServElem;
            existingIncident.NumbRbs = request.NumbRbs;
            existingIncident.EquipmentTypeId = request.EquipmentTypeId;
            existingIncident.AlarmTypeId = request.AlarmTypeId;
            existingIncident.Descr = request.Descr;
            existingIncident.BeginTime = beginUtc;
            existingIncident.EndTime = endUtc;
            existingIncident.Akb = akbSpan;
            existingIncident.Dgu = dguSpan;
            existingIncident.Lenght = formattedDuration;
            existingIncident.Prostoy = prostoySpan;
            existingIncident.CommonProstoy = formattedDuration;
            existingIncident.AlarmWho = request.AlarmWho;
            existingIncident.DescNotice = request.DescNotice;
            existingIncident.AdminDistrict = request.AdminDistrict;
            existingIncident.RegionId = request.RegionId;
            existingIncident.Severity = severity;
            existingIncident.Category = category;
            existingIncident.UpdatedAt = DateTime.UtcNow;

            _db.SaveChanges(); // EF Core выполнит UPDATE incidents SET ... WHERE incident_id = id

            // 6. Пишем в audit_log
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            uint? currentUserId = uint.TryParse(userIdStr, out uint uid) ? uid : null;
            var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var currentRole = User.FindFirst(ClaimTypes.Role)?.Value;

            _audit.Log("update_incident", "incident", existingIncident.IncidentId);

            // 7. Загружаем обновленный DTO через представление v_incidents_full и возвращаем 200 OK
            var updatedItem = (from i in _db.Incidents.AsNoTracking()
                               join v1 in _db.VIncidentsFulls.AsNoTracking() on i.IncidentId equals v1.IncidentId
                               where i.IncidentId == id
                               select new { Entity = i, View = v1 }).First();

            var e = updatedItem.Entity;
            var v = updatedItem.View;

            return Ok(new
            {
                incidentId = e.IncidentId,
                pw = e.Pw,
                directionId = e.DirectionId,
                directionCode = v.DirectionCode,
                techCenterId = e.TechCenterId,
                techCenterCode = v.TechCenterCode,
                priorityId = e.PriorityId,
                priorityCode = v.PriorityCode,
                networkTypeId = e.NetworkTypeId,
                networkTypeCode = v.NetworkTypeCode,
                equipmentTypeId = e.EquipmentTypeId,
                equipmentTypeCode = v.EquipmentTypeCode,
                alarmTypeId = e.AlarmTypeId,
                alarmTypeCode = v.AlarmTypeCode,
                networkElementId = e.NetworkElementId,
                networkElementName = v.NetworkElementName,
                networkElementType = v.NetworkElementType,
                servElem = e.ServElem,
                numbRbs = e.NumbRbs,
                descr = e.Descr,
                beginTime = e.BeginTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                endTime = e.EndTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                akb = FormatTimeSpan(e.Akb),
                dgu = FormatTimeSpan(e.Dgu),
                lenght = e.Lenght,
                prostoy = FormatTimeSpan(e.Prostoy),
                commonProstoy = e.CommonProstoy,
                alarmWho = e.AlarmWho,
                descNotice = e.DescNotice,
                adminDistrict = e.AdminDistrict,
                regionId = e.RegionId,
                severity = e.Severity,
                category = e.Category,
                createdByUserId = e.CreatedByUserId,
                createdAt = e.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                updatedAt = e.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }

        // =========================================================================
        // 2.5. DELETE /api/incidents/{id}
        // =========================================================================
        [HttpDelete("{id}")]
        [Authorize(Roles = "operator,admin")] // Auth: только operator или admin (engineer получит 403 Forbidden)
        public IActionResult DeleteIncident(ulong id)
        {
            // 1. Проверяем существование инцидента
            var incident = _db.Incidents.FirstOrDefault(i => i.IncidentId == id);
            if (incident == null)
            {
                return NotFound(new { error = "not_found" });
            }

            // 2. Сбрасываем привязку в таблице anomalies, чтобы не сломать внешний ключ (fk_an_incident)
            var linkedAnomalies = _db.Anomalies
                .Where(a => a.ExplainedByIncident == id)
                .ToList();

            foreach (var anomaly in linkedAnomalies)
            {
                anomaly.ExplainedByIncident = null;
                // Возвращаем статус аномалии в 'new', так как объясняющий инцидент удален
                if (anomaly.Status == "explained")
                {
                    anomaly.Status = "new";
                }
            }

            // 3. Удаляем сам инцидент и сохраняем изменения в одной транзакции EF Core
            _db.Incidents.Remove(incident);
            _db.SaveChanges();

            // 4. Записываем событие в audit_log
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            uint? currentUserId = uint.TryParse(userIdStr, out uint uid) ? uid : null;
            var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var currentRole = User.FindFirst(ClaimTypes.Role)?.Value;

            _audit.Log("delete_incident", "incident", id);

            // 5. Возвращаем 204 No Content с пустым телом
            return NoContent();
        }


        // =========================================================================
        // 2.6. GET /api/incidents/export
        // =========================================================================
        [HttpGet("export")]
        public IActionResult ExportIncidents(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
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

            // 2. Выгружаем данные из представления v_incidents_full за период
            var items = _db.VIncidentsFulls
                .AsNoTracking()
                .Where(v => v.BeginTime >= fromUtc && v.BeginTime <= toUtc)
                .OrderByDescending(v => v.BeginTime)
                .ToList();

            // 3. Формируем содержимое CSV-файла
            var sb = new StringBuilder();

            // Заголовок таблицы строго по ТЗ
            sb.AppendLine("Incident ID;PW;Направление;ТЦ;Приоритет;Тип сети;Сетевой элемент;Кол-во БС;Тип оборуд.;Тип аварии;Описание;Начало;Окончание;АКБ;ДГУ;Длительность;Простой;Общий простой;Уведомление;Комментарий;Район;Severity;Category");

            foreach (var v in items)
            {
                // Приоритет отдаем ServElem, если пусто — берем NetworkElementName
                var elementName = !string.IsNullOrEmpty(v.ServElem) ? v.ServElem : v.NetworkElementName;

                var row = new[]
                {
            v.IncidentId.ToString(),
            EscapeCsv(v.Pw),
            EscapeCsv(v.DirectionCode),
            EscapeCsv(v.TechCenterCode),
            EscapeCsv(v.PriorityCode),
            EscapeCsv(v.NetworkTypeCode),
            EscapeCsv(elementName),
            EscapeCsv(v.NumbRbs),
            EscapeCsv(v.EquipmentTypeCode),
            EscapeCsv(v.AlarmTypeCode),
            EscapeCsv(v.Descr),
            v.BeginTime.ToString("yyyy-MM-dd HH:mm"),
            v.EndTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
            FormatTimeSpan(v.Akb) ?? "",
            FormatTimeSpan(v.Dgu) ?? "",
            EscapeCsv(v.Lenght),
            FormatTimeSpan(v.Prostoy) ?? "",
            EscapeCsv(v.CommonProstoy),
            EscapeCsv(v.AlarmWho),
            EscapeCsv(v.DescNotice),
            EscapeCsv(v.AdminDistrict),
            EscapeCsv(v.Severity),
            EscapeCsv(v.Category)
        };

                sb.AppendLine(string.Join(";", row));
            }

            // 4. Кодируем в UTF-8 с BOM (new UTF8Encoding(true)), чтобы русский Excel открыл кириллицу без кракозябр
            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var csvBytes = utf8WithBom.GetBytes(sb.ToString());

            // Формируем имя файла по датам периода: incidents_2026-11-01_2026-11-14.csv
            var fileName = $"incidents_{fromUtc:yyyy-MM-dd}_{toUtc:yyyy-MM-dd}.csv";

            _audit.Log("export_incidents", "incident", null);

            // Метод File() сам проставит Content-Type и Content-Disposition: attachment; filename="..."
            return File(csvBytes, "text/csv; charset=utf-8", fileName);
        }

        // =========================================================================
        // 2.7. GET /api/incidents/{id}/anomalies
        // =========================================================================
        [HttpGet("{id}/anomalies")]
        public IActionResult GetIncidentAnomalies(ulong id)
        {
            // 1. Проверяем, существует ли сам инцидент
            bool incidentExists = _db.Incidents.Any(i => i.IncidentId == id);
            if (!incidentExists)
            {
                return NotFound(new { error = "not_found" });
            }

            // 2. Загружаем все аномалии, объясненные этим инцидентом, подтягивая сектор и код БС
            var rawAnomalies = (from a in _db.Anomalies.AsNoTracking()
                                join c in _db.Cells.AsNoTracking() on a.CellId equals c.CellId into cellGroup
                                from cell in cellGroup.DefaultIfEmpty()
                                join ne in _db.NetworkElements.AsNoTracking() on cell.BtsId equals ne.Id into neGroup
                                from netElem in neGroup.DefaultIfEmpty()
                                where a.ExplainedByIncident == id
                                orderby a.TsFrom descending
                                select new
                                {
                                    Anomaly = a,
                                    BtsCode = netElem != null ? netElem.Code : null,
                                    Sector = cell != null ? cell.Sector : null
                                }).ToList();

            // 3. Маппинг в точный JSON-формат контракта
            var result = rawAnomalies.Select(x => new
            {
                anomalyId = x.Anomaly.AnomalyId,
                cellId = x.Anomaly.CellId,
                btsCode = x.BtsCode,
                sector = x.Sector,
                anomalyType = x.Anomaly.AnomalyType,
                kpiCode = x.Anomaly.KpiCode,
                severity = x.Anomaly.Severity,
                tsFrom = x.Anomaly.TsFrom.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                tsTo = x.Anomaly.TsTo.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                observedValue = x.Anomaly.ObservedValue,
                expectedValue = x.Anomaly.ExpectedValue,
                status = x.Anomaly.Status
            }).ToList();

            return Ok(result);
        }


        // =========================================================================
        // Вспомогательные методы (добавьте в конец IncidentsController)
        // =========================================================================

        // Парсит строки вида "01:23:45" или "48:00:00" в TimeSpan
        private static TimeSpan? ParseTimeSpan(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var parts = input.Trim().Split(':');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int hours) &&
                int.TryParse(parts[1], out int minutes) &&
                int.TryParse(parts[2], out int seconds))
            {
                return new TimeSpan(hours, minutes, seconds);
            }

            if (TimeSpan.TryParse(input, out var parsed))
                return parsed;

            return null;
        }

        
        // Вспомогательный метод для корректного вывода интервалов > 24 часов (например, "48:00:00")
        private string? FormatTimeSpan(TimeSpan? ts)
        {
            if (!ts.HasValue) return null;
            return $"{(int)ts.Value.TotalHours:D2}:{ts.Value.Minutes:D2}:{ts.Value.Seconds:D2}";
        }

        // =========================================================================
        // Вспомогательный метод для экранирования спецсимволов в CSV
        // =========================================================================
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // Если в тексте есть точка с запятой, кавычки или перенос строки — оборачиваем в двойные кавычки
            if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

    }
}


public class CreateIncidentRequest
{
    public string? Pw { get; set; }
    public ushort? DirectionId { get; set; }
    public ushort? TechCenterId { get; set; }
    public ushort? PriorityId { get; set; }
    public ushort? NetworkTypeId { get; set; }
    public uint? NetworkElementId { get; set; }
    public string? ServElem { get; set; }
    public string? NumbRbs { get; set; }
    public ushort? EquipmentTypeId { get; set; }
    public ushort? AlarmTypeId { get; set; }
    public string? Descr { get; set; }
    public DateTime BeginTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Akb { get; set; }
    public string? Dgu { get; set; }
    public string? Prostoy { get; set; }
    public string? AlarmWho { get; set; }
    public string? DescNotice { get; set; }
    public string? AdminDistrict { get; set; }
    public ushort? RegionId { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
}
