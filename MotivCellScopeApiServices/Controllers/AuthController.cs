using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Data;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Controllers;
[ApiController, Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration config;
    private readonly MotivReportDbContext legacyDb;
    private readonly AccidentStore store;
    public AuthController(IConfiguration config, MotivReportDbContext legacyDb, AccidentStore store)
    {
        this.config = config;
        this.legacyDb = legacyDb;
        this.store = store;
    }

    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(request.Password) > 72)
            return Unauthorized(new ProblemDetails { Title = "Неверный логин или пароль.", Status = 401 });
        JournalUser? user;
        string? hash;
        if (config.GetValue<bool>("AccidentJournal:Enabled"))
            (user, hash) = await store.LoginUser(request.Username, ct);
        else
        {
            var row = await legacyDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == request.Username, ct);
            user = row == null ? null : new((int)row.UserId, row.Username, row.Username == "admin" ? "admin" : "engineer", true, 1);
            hash = row?.PasswordHash;
        }

        bool valid = false;
        try
        {
            valid = user != null && user.Active && hash != null && BCrypt.Net.BCrypt.Verify(request.Password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
        }

        if (!valid)
            return Unauthorized(new ProblemDetails { Title = "Неверный логин или пароль.", Status = 401 });
        var expires = DateTimeOffset.UtcNow.AddHours(2);
        var claims = new[]
        {
            new Claim("sub", user!.Username),
            new Claim("uid", user.UserId.ToString()),
            new Claim("unique_name", user.Username),
            new Claim("role", user.Role),
            new Claim("ver", user.Version.ToString()),
            new Claim("jti", Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: expires.UtcDateTime, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires, username = user.Username, role = user.Role, userId = user.UserId });
    }

    [HttpGet("me"), Authorize]
    public IActionResult Me() => Ok(new { userId = int.TryParse(User.FindFirstValue("uid"), out var id) ? id : 0, username = User.Identity!.Name, role = User.FindFirstValue("role") });
}

public class LoginRequest
{
    [Required, StringLength(64)]
    public string Username { get; set; } = "";

    [Required, StringLength(100)]
    public string Password { get; set; } = "";
}
