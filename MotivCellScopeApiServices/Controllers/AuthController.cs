using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MotivCellScopeApiServices.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MotivCellScopeApiServices.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {

        private readonly IConfiguration _config;
        private readonly MotivReportDbContext _db;

        public AuthController(IConfiguration config,MotivReportDbContext db)
        {
            _config = config;
            _db = db;
        }

        // POST /api/auth/login
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // Получаем IP-адрес клиента для записи в audit_log
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // 1. Ищем пользователя в таблице users
            var user = _db.Users.FirstOrDefault(u => u.Username == request.Username);

            // 2. Проверяем наличие пользователя и валидность BCrypt-хеша
            bool isPasswordValid = user != null &&
                                   !string.IsNullOrEmpty(user.PasswordHash) &&
                                   BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            if (!isPasswordValid)
            {
                // Пишем неудачную попытку входа в audit_log (согласно схеме: action='login', success=0)
                WriteLoginAudit(user?.UserId, request.Username, user?.Role, ipAddress, success: false);
                return Unauthorized(new { message = "Неверный логин или пароль" });
            }

            // 3. Пишем успешный вход в audit_log
            WriteLoginAudit(user!.UserId, user.Username, user.Role, ipAddress, success: true);

            // 4. Генерируем JWT-токен с зашитой внутри ролью
            var expiresAt = DateTime.UtcNow.AddHours(2);
            var token = GenerateJwtToken(user, expiresAt);

            return Ok(new
            {
                token,
                expiresAt = expiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                username = user.Username,
                role = user.Role 
            });
        }

        // GET /api/auth/me
        [HttpGet("me")]
        [Authorize] // Разрешает доступ с валидным токеном для любой роли (engineer, operator, admin)
        public IActionResult GetCurrentUser()
        {
            // Достаем данные пользователя из Claims расшифрованного JWT-токена
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            // Если токен поврежден и в нем нет нужных полей
            if (!uint.TryParse(userIdStr, out uint userId) || string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { message = "Некорректные данные токена" });
            }

            // Возвращаем JSON строго в нужном формате
            return Ok(new
            {
                userId,
                username,
                role
            });
        }


        private string GenerateJwtToken(User user, DateTime expires)
        {
            var jwtKey = _config["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key не настроен");
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            // Кладём в токен ID, имя и РОЛЬ пользователя
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role), // <-- Ключевой клейм для работы [Authorize(Roles = "...")]
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        private void WriteLoginAudit(uint? userId, string username, string? role, string? ip, bool success)
        {
            try
            {
                // Используем модель AuditLog, которая сгенерировалась при скаффолдинге
                var log = new AuditLog
                {
                    Ts = DateTime.UtcNow,
                    UserId = userId,
                    Username = username,
                    Role = role,
                    Action = "login", // Значение из ENUM таблицы audit_log
                    EntityType = "user",
                    EntityId = userId,
                    IpAddress = ip,
                    Success = success // Если EF сгенерировал sbyte/bool, поправьте на (sbyte)(success ? 1 : 0) при необходимости
                };

                _db.AuditLogs.Add(log);
                _db.SaveChanges();
            }
            catch
            {
                // Не роняем авторизацию при сбое логирования
                //TODO сделать внутренний лог с ошибками
            }
        }

    }

    // Модель входящего запроса
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
