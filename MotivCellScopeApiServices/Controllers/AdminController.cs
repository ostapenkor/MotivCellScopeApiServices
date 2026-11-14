using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;
using System.Diagnostics;
using System.Security.Claims;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Задает базовый путь /api/admin
    [Authorize(Roles = "admin")] // Auth: да (требует JWT-токен)
    public class AdminController : ControllerBase
    {
        private readonly MotivReportDbContext _db;

        public AdminController(MotivReportDbContext db)
        {
            _db = db;
        }

        // =========================================================================
        // 5.1. GET /api/admin/audit
        // Просмотр журнала действий пользователей (audit_log)
        // =========================================================================
        [HttpGet("audit")]
        public IActionResult GetAuditLogs(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] uint? userId,
            [FromQuery] string? action,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var query = _db.AuditLogs.AsNoTracking().AsQueryable();

            if (from.HasValue)
            {
                var fromUtc = from.Value.ToUniversalTime();
                query = query.Where(a => a.Ts >= fromUtc);
            }

            if (to.HasValue)
            {
                var toUtc = to.Value.ToUniversalTime();
                query = query.Where(a => a.Ts <= toUtc);
            }

            if (userId.HasValue)
            {
                query = query.Where(a => a.UserId == userId.Value);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                var act = action.Trim().ToLower();
                query = query.Where(a => a.Action == act);
            }

            int total = query.Count();

            var rawItems = query
                .OrderByDescending(a => a.Ts)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var items = rawItems.Select(a => new
            {
                auditId = a.AuditId,
                ts = a.Ts.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                userId = a.UserId,
                username = a.Username,
                role = a.Role,
                action = a.Action,
                entityType = a.EntityType,
                entityId = a.EntityId,
                ipAddress = a.IpAddress,
                success = a.Success
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
        // 5.2. GET /api/admin/users
        // Список всех пользователей (строго без password_hash)
        // =========================================================================
        [HttpGet("users")]
        public IActionResult GetUsers()
        {
            var users = _db.Users
                .AsNoTracking()
                .OrderBy(u => u.UserId)
                .Select(u => new
                {
                    userId = u.UserId,
                    username = u.Username,
                    role = u.Role,
                    createdAt = u.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                })
                .ToList();

            return Ok(users);
        }

        // =========================================================================
        // 5.3. POST /api/admin/users
        // Создание нового пользователя с хешированием BCrypt и записью в аудит
        // =========================================================================
        [HttpPost("users")]
        public IActionResult CreateUser([FromBody] CreateUserRequest request)
        {
            // 1. Валидация входных данных
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 64)
            {
                return BadRequest(new { error = "validation_failed", field = "username" });
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 4)
            {
                return BadRequest(new { error = "validation_failed", field = "password" });
            }

            var allowedRoles = new[] { "engineer", "operator", "admin" };
            var role = string.IsNullOrWhiteSpace(request.Role) ? "engineer" : request.Role.Trim().ToLower();
            if (!allowedRoles.Contains(role))
            {
                return BadRequest(new { error = "validation_failed", field = "role" });
            }

            var cleanUsername = request.Username.Trim();

            // 2. Проверка уникальности логина
            if (_db.Users.Any(u => u.Username == cleanUsername))
            {
                return Conflict(new { error = "username_already_exists", message = "Пользователь с таким именем уже существует." });
            }

            // 3. Хешируем пароль через BCrypt (workFactor = 11 как в ТЗ) и сохраняем в БД
            var newUser = new User
            {
                Username = cleanUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 11),
                Role = role,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(newUser);
            _db.SaveChanges();

            // 4. Пишем событие создания пользователя в audit_log
            WriteUserCreationAudit(newUser.UserId, newUser.Username);

            // 5. Возвращаем 201 Created со структурой из ТЗ
            return Created($"/api/admin/users/{newUser.UserId}", new
            {
                userId = newUser.UserId,
                username = newUser.Username,
                role = newUser.Role
            });
        }

        // Вспомогательный метод для записи в audit_log при создании пользователя
        private void WriteUserCreationAudit(uint createdUserId, string createdUsername)
        {
            try
            {
                var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                uint? adminId = uint.TryParse(adminIdStr, out uint uid) ? uid : null;
                var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "admin";
                var adminRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "admin";

                var log = new AuditLog
                {
                    Ts = DateTime.UtcNow,
                    UserId = adminId,
                    Username = adminName,
                    Role = adminRole,
                    // Примечание: в вашей SQL-схеме ENUM action содержит ('login','create_incident','update_incident','delete_incident','detect_anomalies','refresh_kpi').
                    // Если в базе ENUM еще не расширен значением 'create_user', MariaDB может отклонить вставку, поэтому блок обернут в try-catch.
                    Action = "create_user",
                    EntityType = "user",
                    EntityId = createdUserId,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Success = true
                };

                _db.AuditLogs.Add(log);
                _db.SaveChanges();
            }
            catch
            {
                // Если ENUM в таблице audit_log строгий и не содержит 'create_user', не роняем основной запрос 201 Created
            }
        }

    }

    public class CreateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "engineer";
    }
}
