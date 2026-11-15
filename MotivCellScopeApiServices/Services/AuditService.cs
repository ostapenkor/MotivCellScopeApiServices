using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using System.Security.Claims;

namespace MotivCellScopeApiServices.Services
{
    public class AuditService
    {
        private readonly MotivReportDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditService> _logger;

        // Список строго разрешенных значений ENUM из вашей базы данных
        private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "login",
            "create_user",
            "create_incident",
            "update_incident",
            "delete_incident",
            "explain_anomaly",
            "update_anomaly_status",
            "export_incidents",
            "export_report",
            "view_audit_log",
            "view_users",
            "detect_anomalies",
            "detect_availability_anomalies",
            "refresh_kpi"
        };

        public AuditService(
            MotivReportDbContext db,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuditService> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        /// <summary>
        /// Универсальный синхронный метод записи действия в audit_log
        /// </summary>
        /// <param name="action">Код действия из списка ENUM</param>
        /// Тип сущности (user, incident, anomaly, report, audit)</param>
        /// ID основной сущности</param>
        /// <param name="details">Любой анонимный объект с деталями (сериализуется в JSON)</param>
        /// <param name="success">Результат операции</param>
        /// <param name="overrideUsername">Явное имя (нужно только для login)</param>
        /// <param name="overrideUserId">Явный ID (нужно только для login)</param>
        /// <param name="overrideRole">Явная роль (нужно только для login)</param>
        public void Log(
            string action,
            string? entityType = null,
            ulong? entityId = null,
            bool success = true,
            string? overrideUsername = null,
            uint? overrideUserId = null,
            string? overrideRole = null)
        {
            AuditLog? logEntry = null;
            try
            {
                var normalizedAction = action.Trim().ToLower().Replace("-", "_");
                if (!AllowedActions.Contains(normalizedAction))
                {
                    _logger.LogWarning("Попытка записать неизвестный action '{Action}' в audit_log.", action);
                    return;
                }

                var context = _httpContextAccessor.HttpContext;
                var userClaims = context?.User;

                // 1. Определяем ID пользователя
                uint? userId = overrideUserId;
                if (!userId.HasValue && userClaims != null)
                {
                    var idStr = userClaims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (uint.TryParse(idStr, out uint parsedId))
                    {
                        userId = parsedId;
                    }
                }

                // 2. Определяем логин и роль
                var username = overrideUsername
                               ?? userClaims?.FindFirst(ClaimTypes.Name)?.Value
                               ?? "system";
                if (username.Length > 64) username = username[..64];

                var role = overrideRole ?? userClaims?.FindFirst(ClaimTypes.Role)?.Value;
                if (role != null && role.Length > 16) role = role[..16];

                // 3. Безопасно получаем IP-адрес
                var rawIp = context?.Connection.RemoteIpAddress?.ToString();
                if (rawIp != null && rawIp.Length > 45) rawIp = rawIp[..45];


                logEntry = new AuditLog
                {
                    Ts = DateTime.UtcNow,
                    UserId = userId,
                    Username = username,
                    Role = role,
                    Action = normalizedAction,
                    EntityType = entityType,
                    EntityId = entityId,
                    IpAddress = rawIp,
                    Success = success
                };

                _db.AuditLogs.Add(logEntry);
                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                if (logEntry != null)
                    _db.Entry(logEntry).State = EntityState.Detached;
                // Пишем реальную ошибку в консоль Docker, а не глушим в пустом catch
                _logger.LogError(ex, "Ошибка записи в audit_log для действия '{Action}'", action);
            }
        }
    }
}
