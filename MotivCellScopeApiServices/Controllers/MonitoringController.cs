using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using System.Diagnostics;
using System.Security.Claims;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MonitoringController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        public MonitoringController(MotivReportDbContext db)
        {
            _db = db;
        }

        // =========================================================================
        // 4.1. GET /api/monitoring/summary
        // =========================================================================
        [HttpGet("summary")]
        public IActionResult GetSummary(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] ushort? regionId)
        {
            var toUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var fromUtc = from?.ToUniversalTime() ?? toUtc.AddHours(-24);

            if (fromUtc > toUtc)
            {
                return BadRequest(new { message = "Дата 'from' не может быть больше 'to'." });
            }

            // 1. Агрегация KPI за период
            var kpiQuery = from k in _db.KpiCellHourlies.AsNoTracking()
                           join c in _db.Cells.AsNoTracking() on k.CellId equals c.CellId
                           join ne in _db.NetworkElements.AsNoTracking() on c.BtsId equals ne.Id
                           where k.TsHour >= fromUtc && k.TsHour <= toUtc
                           select new { Kpi = k, RegionId = ne.RegionId };

            if (regionId.HasValue)
            {
                kpiQuery = kpiQuery.Where(x => x.RegionId == regionId.Value);
            }

            var kpiStats = kpiQuery
                .GroupBy(x => 1)
                .Select(g => new
                {
                    AvgCssr = g.Average(x => (double?)x.Kpi.CssrPct) ?? 0,
                    AvgDcr = g.Average(x => (double?)x.Kpi.DcrPct) ?? 0,
                    TotalTrafficBytes = g.Sum(x => (double)(x.Kpi.TrafficDlBytes + x.Kpi.TrafficUlBytes)),
                    TotalRows = g.Count(),
                    AvailableRows = g.Count(x => x.Kpi.ConnAttempt > 0)
                })
                .FirstOrDefault();

            double avgAvailability = (kpiStats != null && kpiStats.TotalRows > 0)
                ? (double)kpiStats.AvailableRows / kpiStats.TotalRows * 100.0
                : 100.0;

            double totalTrafficGb = (kpiStats?.TotalTrafficBytes ?? 0) / (1024.0 * 1024.0 * 1024.0);

            // 2. Сводка по базовым станциям (BTS) и сотам (Cells)
            var btsQuery = _db.NetworkElements.AsNoTracking().Where(ne => ne.ElementType == "bts");
            var cellsQuery = from c in _db.Cells.AsNoTracking()
                             join ne in _db.NetworkElements.AsNoTracking() on c.BtsId equals ne.Id
                             select new { Cell = c, RegionId = ne.RegionId };

            if (regionId.HasValue)
            {
                btsQuery = btsQuery.Where(ne => ne.RegionId == regionId.Value);
                cellsQuery = cellsQuery.Where(x => x.RegionId == regionId.Value);
            }

            var btsStats = btsQuery
                .GroupBy(b => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Active = g.Count(x => x.Status == "active"),
                    Problem = g.Count(x => x.Status == "down" || x.Status == "degraded"),
                    Maintenance = g.Count(x => x.Status == "maintenance")
                })
                .FirstOrDefault();

            int cellsTotal = cellsQuery.Count();

            // 3. Счетчики аномалий за период
            var anomaliesQuery = from a in _db.Anomalies.AsNoTracking()
                                 join c in _db.Cells.AsNoTracking() on a.CellId equals c.CellId into cg
                                 from cell in cg.DefaultIfEmpty()
                                 join ne in _db.NetworkElements.AsNoTracking() on cell.BtsId equals ne.Id into neg
                                 from netElem in neg.DefaultIfEmpty()
                                 where a.TsFrom >= fromUtc && a.TsFrom <= toUtc
                                 select new { Anomaly = a, RegionId = netElem != null ? netElem.RegionId : null };

            if (regionId.HasValue)
            {
                anomaliesQuery = anomaliesQuery.Where(x => x.RegionId == regionId.Value);
            }

            var anomStats = anomaliesQuery
                .GroupBy(x => 1)
                .Select(g => new
                {
                    Open = g.Count(x => (x.Anomaly.Status == "new" || x.Anomaly.Status == "acknowledged") && x.Anomaly.ExplainedByIncident == null),
                    Explained = g.Count(x => x.Anomaly.Status == "explained" || x.Anomaly.ExplainedByIncident != null),
                    Critical = g.Count(x => x.Anomaly.Severity == "critical" && x.Anomaly.Status != "resolved" && x.Anomaly.Status != "false_positive")
                })
                .FirstOrDefault();

            return Ok(new
            {
                period = new
                {
                    from = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    to = toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
                },
                avgCssr = Math.Round(kpiStats?.AvgCssr ?? 0, 2),
                avgDcr = Math.Round(kpiStats?.AvgDcr ?? 0, 2),
                avgAvailability = Math.Round(avgAvailability, 2),
                totalTrafficGb = Math.Round(totalTrafficGb, 1),
                btsTotal = btsStats?.Total ?? 0,
                btsActive = btsStats?.Active ?? 0,
                btsProblem = btsStats?.Problem ?? 0,
                btsMaintenance = btsStats?.Maintenance ?? 0,
                cellsTotal = cellsTotal,
                anomaliesOpen = anomStats?.Open ?? 0,
                anomaliesExplained = anomStats?.Explained ?? 0,
                anomaliesCritical = anomStats?.Critical ?? 0
            });
        }

        // =========================================================================
        // 4.2. GET /api/monitoring/network-elements
        // =========================================================================
        [HttpGet("network-elements")]
        public IActionResult GetNetworkElements(
            [FromQuery] ushort? regionId,
            [FromQuery] string? elementType,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            var toUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var fromUtc = from?.ToUniversalTime() ?? toUtc.AddHours(-24);

            // 1. Загружаем все сетевые элементы с привязкой к BTS, регионам, ТЦ и направлениям
            var neQuery = from ne in _db.NetworkElements.AsNoTracking()
                          join b in _db.Bts.AsNoTracking() on ne.Id equals b.BtsId into bg
                          from bts in bg.DefaultIfEmpty()
                          join r in _db.Regions.AsNoTracking() on ne.RegionId equals r.RegionId into rg
                          from reg in rg.DefaultIfEmpty()
                          join tc in _db.TechnicalCenters.AsNoTracking() on ne.TechCenterId equals tc.TechCenterId into tcg
                          from tech in tcg.DefaultIfEmpty()
                          join d in _db.Directions.AsNoTracking() on tech.DirectionId equals d.DirectionId into dg
                          from dir in dg.DefaultIfEmpty()
                          select new
                          {
                              Ne = ne,
                              Bts = bts,
                              RegionCode = reg != null ? reg.Code : null,
                              TechCenterCode = tech != null ? tech.Code : null,
                              DirectionCode = dir != null ? dir.Code : null
                          };

            if (regionId.HasValue)
            {
                neQuery = neQuery.Where(x => x.Ne.RegionId == regionId.Value);
            }

            if (!string.IsNullOrWhiteSpace(elementType))
            {
                var type = elementType.Trim().ToLower();
                neQuery = neQuery.Where(x => x.Ne.ElementType == type);
            }

            var elements = neQuery.ToList();

            // 2. Считаем количество сот по каждой БС одним запросом
            var cellCounts = _db.Cells.AsNoTracking()
                .GroupBy(c => c.BtsId)
                .Select(g => new { BtsId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.BtsId, x => x.Count);

            // 3. Агрегируем открытые/активные аномалии за период по каждой БС
            var rawAnomalies = (from a in _db.Anomalies.AsNoTracking()
                                join c in _db.Cells.AsNoTracking() on a.CellId equals c.CellId
                                where a.TsFrom >= fromUtc && a.TsFrom <= toUtc
                                      && a.Status != "resolved" && a.Status != "false_positive"
                                select new
                                {
                                    c.BtsId,
                                    a.Severity,
                                    a.KpiCode
                                }).ToList();

            var anomaliesByBts = rawAnomalies
                .GroupBy(x => x.BtsId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        // Определяем наивысший уровень критичности (critical > warning > info)
                        var worstItem = g
                            .OrderByDescending(x => GetSeverityWeight(x.Severity))
                            .First();
                        return new
                        {
                            Count = g.Count(),
                            WorstSeverity = worstItem.Severity,
                            WorstKpi = worstItem.KpiCode
                        };
                    });
            // 4. Собираем финальный список
            var result = elements.Select(x =>
            {
                var id = x.Ne.Id;
                cellCounts.TryGetValue(id, out int cCount);
                anomaliesByBts.TryGetValue(id, out var anomInfo);
                return new
                {
                    id = id,
                    code = x.Ne.Code,
                    networkElementCode = x.Ne.NetworkElementCode,
                    fullName = x.Ne.FullName,
                    shortName = x.Ne.ShortName,
                    elementType = x.Ne.ElementType,
                    regionId = x.Ne.RegionId,
                    regionCode = x.RegionCode,
                    techCenterId = x.Ne.TechCenterId,
                    techCenterCode = x.TechCenterCode,
                    directionCode = x.DirectionCode,
                    status = x.Ne.Status,
                    lat = x.Bts?.Lat,
                    lon = x.Bts?.Lon,
                    siteType = x.Bts?.SiteType,
                    cellsCount = cCount,
                    anomalySeverity = anomInfo?.WorstSeverity,
                    anomalyCount = anomInfo?.Count ?? 0,
                    worstKpiCode = anomInfo?.WorstKpi
                };
            }).ToList();
            return Ok(result);
        }
        // =========================================================================
        // 4.3. GET /api/monitoring/network-elements/{id}
        // =========================================================================
        [HttpGet("network-elements/{id}")]
        public IActionResult GetNetworkElementById(uint id)
        {
            var element = (from ne in _db.NetworkElements.AsNoTracking()
                           join b in _db.Bts.AsNoTracking() on ne.Id equals b.BtsId into bg
                           from bts in bg.DefaultIfEmpty()
                           join r in _db.Regions.AsNoTracking() on ne.RegionId equals r.RegionId into rg
                           from reg in rg.DefaultIfEmpty()
                           join tc in _db.TechnicalCenters.AsNoTracking() on ne.TechCenterId equals tc.TechCenterId into tcg
                           from tech in tcg.DefaultIfEmpty()
                           join d in _db.Directions.AsNoTracking() on tech.DirectionId equals d.DirectionId into dg
                           from dir in dg.DefaultIfEmpty()
                           where ne.Id == id
                           select new
                           {
                               Ne = ne,
                               Bts = bts,
                               RegionCode = reg != null ? reg.Code : null,
                               TechCenterCode = tech != null ? tech.Code : null,
                               DirectionCode = dir != null ? dir.Code : null
                           }).FirstOrDefault();
            if (element == null)
            {
                return NotFound(new { error = "not_found" });
            }
            // Загружаем соты этой БС и считаем их средние KPI за последние 24 часа
            var sinceUtc = DateTime.UtcNow.AddHours(-24);
            var dbCells = _db.Cells.AsNoTracking()
            .Where(c => c.BtsId == id)
            .OrderBy(c => c.LocalCellId)
            .ToList();
            var cellIds = dbCells.Select(c => c.CellId).ToList();
            var kpiByCell = _db.KpiCellHourlies.AsNoTracking()
            .Where(k => cellIds.Contains(k.CellId) && k.TsHour >= sinceUtc)
            .GroupBy(k => k.CellId)
            .Select(g => new
            {
                CellId = g.Key,
                Cssr = g.Average(x => (double?)x.CssrPct) ?? 0,
                Dcr = g.Average(x => (double?)x.DcrPct) ?? 0,
                TrafficBytes = g.Sum(x => (double)(x.TrafficDlBytes + x.TrafficUlBytes)),
                TotalRows = g.Count(),
                AvailRows = g.Count(x => x.ConnAttempt > 0)
            })
            .ToDictionary(x => x.CellId);
            var cellsDto = dbCells.Select(c =>
            {
                kpiByCell.TryGetValue(c.CellId, out var kpi);
                double avail = (kpi != null && kpi.TotalRows > 0) ? (double)kpi.AvailRows / kpi.TotalRows * 100.0 : 100.0;
                double trafGb = (kpi?.TrafficBytes ?? 0) / (1024.0 * 1024.0 * 1024.0);
                return new
                {
                    cellId = c.CellId,
                    localCellId = c.LocalCellId,
                    sector = c.Sector,
                    cssr = Math.Round(kpi?.Cssr ?? 0, 2),
                    dcr = Math.Round(kpi?.Dcr ?? 0, 2),
                    availability = Math.Round(avail, 2),
                    trafficGb = Math.Round(trafGb, 2)
                };
            }).ToList();
            // Загружаем активные аномалии по сотам этого элемента
            var anomaliesDto = _db.Anomalies.AsNoTracking()
            .Where(a => a.CellId.HasValue && cellIds.Contains(a.CellId.Value)
            && a.Status != "resolved" && a.Status != "false_positive")
            .OrderByDescending(a => a.DetectedAt)
            .Select(a => new
            {
                anomalyId = a.AnomalyId,
                type = a.AnomalyType,
                kpiCode = a.KpiCode,
                severity = a.Severity,
                status = a.Status
            })
            .ToList();
            var neEntity = element.Ne;
            return Ok(new
            {
                id = neEntity.Id,
                code = neEntity.Code,
                fullName = neEntity.FullName,
                elementType = neEntity.ElementType,
                regionCode = element.RegionCode,
                techCenterCode = element.TechCenterCode,
                directionCode = element.DirectionCode,
                status = neEntity.Status,
                statusSince = neEntity.StatusSince?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                stopReason = neEntity.StopReason,
                expectedRestoreDate = neEntity.ExpectedRestoreDate?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                address = element.Bts?.Address,
                lat = element.Bts?.Lat,
                lon = element.Bts?.Lon,
                siteType = element.Bts?.SiteType,
                cells = cellsDto,
                anomalies = anomaliesDto
            });
        }
        // =========================================================================
        // 4.4. GET /api/monitoring/cells
        // =========================================================================
        [HttpGet("cells")]
        public IActionResult GetCells(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] ushort? regionId,
        [FromQuery] uint? btsId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sort = "cssr",
        [FromQuery] string order = "asc")
        {
            var toUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var fromUtc = from?.ToUniversalTime() ?? toUtc.AddHours(-24);
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;
            // 1. Базовая выборка сот
            var cellsQuery = from c in _db.Cells.AsNoTracking()
                             join ne in _db.NetworkElements.AsNoTracking() on c.BtsId equals ne.Id
                             join r in _db.Regions.AsNoTracking() on ne.RegionId equals r.RegionId into rg
                             from reg in rg.DefaultIfEmpty()
                             select new
                             {
                                 Cell = c,
                                 BtsCode = ne.Code,
                                 RegionId = ne.RegionId,
                                 RegionCode = reg != null ? reg.Code : null
                             };
            if (regionId.HasValue)
            {
                cellsQuery = cellsQuery.Where(x => x.RegionId == regionId.Value);
            }
            if (btsId.HasValue)
            {
                cellsQuery = cellsQuery.Where(x => x.Cell.BtsId == btsId.Value);
            }
            var cellsList = cellsQuery.ToList();
            int total = cellsList.Count;
            var cellIds = cellsList.Select(x => x.Cell.CellId).ToList();
            // 2. Агрегация KPI за период для отфильтрованных сот
            var kpiMap = _db.KpiCellHourlies.AsNoTracking()
            .Where(k => cellIds.Contains(k.CellId) && k.TsHour >= fromUtc && k.TsHour <= toUtc)
            .GroupBy(k => k.CellId)
            .Select(g => new
            {
                CellId = g.Key,
                Cssr = g.Average(x => (double?)x.CssrPct) ?? 0,
                Dcr = g.Average(x => (double?)x.DcrPct) ?? 0,
                TrafficBytes = g.Sum(x => (double)(x.TrafficDlBytes + x.TrafficUlBytes)),
                TotalRows = g.Count(),
                AvailRows = g.Count(x => x.ConnAttempt > 0)
            })
            .ToDictionary(x => x.CellId);
            // 3. Агрегация аномалий за период
            var anomMap = _db.Anomalies.AsNoTracking()
            .Where(a => a.CellId.HasValue && cellIds.Contains(a.CellId.Value)
            && a.TsFrom >= fromUtc && a.TsFrom <= toUtc
            && a.Status != "resolved" && a.Status != "false_positive")
            .ToList()
            .GroupBy(a => a.CellId!.Value)
            .ToDictionary(
            g => g.Key,
            g => new
            {
                Count = g.Count(),
                WorstSeverity = g.OrderByDescending(x => GetSeverityWeight(x.Severity)).First().Severity
            });
            // 4. Собираем DTO
            var combined = cellsList.Select(x =>
            {
                var cid = x.Cell.CellId;
                kpiMap.TryGetValue(cid, out var kpi);
                anomMap.TryGetValue(cid, out var anom);
                double avail = (kpi != null && kpi.TotalRows > 0) ? (double)kpi.AvailRows / kpi.TotalRows * 100.0 : 100.0;
                double trafGb = (kpi?.TrafficBytes ?? 0) / (1024.0 * 1024.0 * 1024.0);
                return new
                {
                    cellId = cid,
                    btsCode = x.BtsCode,
                    localCellId = x.Cell.LocalCellId,
                    sector = x.Cell.Sector,
                    regionCode = x.RegionCode,
                    cssr = Math.Round(kpi?.Cssr ?? 0, 2),
                    dcr = Math.Round(kpi?.Dcr ?? 0, 2),
                    availability = Math.Round(avail, 2),
                    trafficGb = Math.Round(trafGb, 2),
                    anomalyCount = anom?.Count ?? 0,
                    worstSeverity = anom?.WorstSeverity
                };
            });
            // 5. Сортировка и пагинация
            bool isAsc = order.Equals("asc", StringComparison.OrdinalIgnoreCase);
            combined = sort.ToLower() switch
            {
                "dcr" => isAsc ? combined.OrderBy(x => x.dcr) : combined.OrderByDescending(x => x.dcr),
                "availability" => isAsc ? combined.OrderBy(x => x.availability) : combined.OrderByDescending(x => x.availability),
                "trafficgb" => isAsc ? combined.OrderBy(x => x.trafficGb) : combined.OrderByDescending(x => x.trafficGb),
                _ => isAsc ? combined.OrderBy(x => x.cssr) : combined.OrderByDescending(x => x.cssr)
            };
            var pagedItems = combined
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
            return Ok(new
            {
                total = total,
                page = page,
                pageSize = pageSize,
                items = pagedItems
            });
        }
        // =========================================================================
        // 4.5. GET /api/monitoring/cells/{id}/kpi
        // =========================================================================
        [HttpGet("cells/{id}/kpi")]
        public IActionResult GetCellKpiSeries(
        uint id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
        {
            if (!from.HasValue || !to.HasValue)
            {
                return BadRequest(new { message = "Параметры 'from' и 'to' обязательны." });
            }
            if (!_db.Cells.Any(c => c.CellId == id))
            {
                return NotFound(new { error = "not_found" });
            }
            var fromUtc = from.Value.ToUniversalTime();
            var toUtc = to.Value.ToUniversalTime();
            var rawPoints = _db.KpiCellHourlies.AsNoTracking()
            .Where(k => k.CellId == id && k.TsHour >= fromUtc && k.TsHour <= toUtc)
            .OrderBy(k => k.TsHour)
            .ToList();
            var series = rawPoints.Select(k =>
            {
                double trafGb = (double)(k.TrafficDlBytes + k.TrafficUlBytes) / (1024.0 * 1024.0 * 1024.0);
                // availability: 1 если были попытки соединений (сота работала), 0 если простой
                int avail = k.ConnAttempt > 0 ? 1 : 0;
                return new
                {
                    tsHour = k.TsHour.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    cssr = Math.Round((double)(k.CssrPct ?? 0), 2),
                    dcr = Math.Round((double)(k.DcrPct ?? 0), 2),
                    trafficGb = Math.Round(trafGb, 2),
                    availability = avail
                };
            }).ToList();
            return Ok(series);
        }
        // =========================================================================
        // 4.8. POST /api/monitoring/detect-anomalies
        // =========================================================================
        [HttpPost("detect-anomalies")]
        [Authorize(Roles = "operator,admin")]
        public IActionResult DetectAnomalies([FromBody] MonitoringDetectRequest request)
        {
            var sw = Stopwatch.StartNew();
            _db.Database.SetCommandTimeout(120);
            int countBefore = _db.Anomalies.Count();
            int affectedRows = _db.Database.ExecuteSqlRaw("CALL sp_detect_anomalies({0});", request.HoursBack);
            int countAfter = _db.Anomalies.Count();
            int newlyFound = Math.Max(0, countAfter - countBefore);
            int processedCount = affectedRows > 0 ? affectedRows : newlyFound;
            WriteAuditLog("detect_anomalies", "anomaly", null);
            sw.Stop();
            return Ok(new
            {
                found = newlyFound,
                processed = processedCount,
                durationMs = sw.ElapsedMilliseconds
            });
        }
        // =========================================================================
        // 4.9. POST /api/monitoring/refresh-kpi
        // =========================================================================
        [HttpPost("refresh-kpi")]
        [Authorize(Roles = "operator,admin")]
        public IActionResult RefreshKpi([FromBody] MonitoringPeriodRequest request)
        {
            var sw = Stopwatch.StartNew();
            _db.Database.SetCommandTimeout(180);
            var fromUtc = request.From.ToUniversalTime();
            var toUtc = request.To.ToUniversalTime();
            var fromStr = fromUtc.ToString("yyyy-MM-dd HH:mm:ss");
            var toStr = toUtc.ToString("yyyy-MM-dd HH:mm:ss");
            int affectedRows = _db.Database.ExecuteSqlRaw("CALL sp_refresh_kpi_hourly({0}, {1});", fromStr, toStr);
            if (affectedRows <= 0)
            {
                affectedRows = _db.KpiCellHourlies.Count(k => k.TsHour >= fromUtc && k.TsHour <= toUtc);
            }
            WriteAuditLog("refresh_kpi", "kpi", null);
            sw.Stop();
            return Ok(new
            {
                processed = affectedRows,
                durationMs = sw.ElapsedMilliseconds
            });
        }
        // =========================================================================
        // Вспомогательные методы
        // =========================================================================
        private static int GetSeverityWeight(string? severity) => severity switch
        {
            "critical" => 3,
            "warning" => 2,
            "info" => 1,
            _ => 0
        };
        private void WriteAuditLog(string action, string entityType, ulong? entityId)
        {
            try
            {
                var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                uint? userId = uint.TryParse(userIdStr, out uint uid) ? uid : null;
                var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
                var role = User.FindFirst(ClaimTypes.Role)?.Value;
                var log = new AuditLog
                {
                    Ts = DateTime.UtcNow,
                    UserId = userId,
                    Username = username,
                    Role = role,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Success = true
                };
                _db.AuditLogs.Add(log);
                _db.SaveChanges();
            }
            catch
            {
                // Не прерываем ответ при ошибке записи аудита
            }
        }
    }
    public class MonitoringDetectRequest
    {
        public int HoursBack { get; set; } = 192;
    }
    public class MonitoringPeriodRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }
}
