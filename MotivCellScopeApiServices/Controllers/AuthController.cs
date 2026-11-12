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
            // 1. Ищем пользователя в базе данных по логину
            // Примечание: проверьте точные имена свойств в вашем классе User.cs (например, Login или Username)
            var user = _db.Users
                .FirstOrDefault(u => u.Username == request.Username);

            // 2. Если пользователь не найден или пароль не совпадает — отказываем в доступе
            // Проверяем, подходит ли введенный пароль под хеш из базы
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "Неверный логин или пароль" });
            }

            // 3. Генерируем токен для найденного пользователя
            var expiresAt = DateTime.UtcNow.AddHours(2);
            var token = GenerateJwtToken(user.Username, expiresAt);

            return Ok(new
            {
                token = token,
                expiresAt = expiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                username = user.Username
            });
        }

        private string GenerateJwtToken(string username, DateTime expires)
        {
            var jwtKey = _config["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key не настроен");
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
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

    }

    // Модель входящего запроса
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
