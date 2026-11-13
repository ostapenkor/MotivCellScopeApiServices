using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Data;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);
if (Directory.Exists("/run/secrets"))
    builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
var config = builder.Configuration;
bool journal = config.GetValue<bool>("AccidentJournal:Enabled");
bool legacy = config.GetValue("Features:LegacyKpi", true);
// Builder корректно экранирует даже пароль с ; и другими специальными символами.
var connection = new MySqlConnectionStringBuilder(config.GetConnectionString("MotivReportConnection") ?? throw new InvalidOperationException("Настройте ConnectionStrings:MotivReportConnection."));
if (config["db_user"] is string dbUser)
    connection.UserID = dbUser;
if (config["db_password"] is string dbPassword)
    connection.Password = dbPassword;
if (journal)
    connection.AllowZeroDateTime = true;
string connectionString = connection.ConnectionString;
config["ConnectionStrings:Journal"] = connectionString;
string jwtKey = config["jwt_key"] ?? config["Jwt:Key"] ?? throw new InvalidOperationException("Настройте jwt_key или Jwt:Key.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("JWT-ключ должен быть не короче 32 байт.");
config["Jwt:Key"] = jwtKey;
// Версия задаётся явно: старт не открывает соединение с ещё не готовой БД.
builder.Services.AddDbContext<MotivReportDbContext>(o => o.UseMySql(connectionString, new MariaDbServerVersion(config["Database:Version"] ?? "11.0"), mysql => mysql.EnableRetryOnFailure()));
builder.Services.AddScoped<AccidentStore>();
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new ControllerProfileConvention(journal, legacy));
}).AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new UtcDateConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "Bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = config["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        NameClaimType = "unique_name",
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(15)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!journal)
                return;
            var claims = context.Principal!;
            if (!int.TryParse(claims.FindFirst("uid")?.Value, out int id))
            {
                context.Fail("Нужен повторный вход.");
                return;
            }

            var user = await context.HttpContext.RequestServices.GetRequiredService<AccidentStore>().User(id, context.HttpContext.RequestAborted);
            // Изменение прав или отключение сразу отзывают ранее выданный JWT.
            if (user == null || !user.Active || claims.FindFirst("ver")?.Value != user.Version.ToString() || claims.FindFirst("role")?.Value != user.Role)
                context.Fail("Права изменены. Войдите заново.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context => await Results.Problem(statusCode: 500, title: "Не удалось выполнить запрос. Проверьте журнал сервера.").ExecuteAsync(context)));
app.UseStatusCodePages(async status =>
{
    if (status.HttpContext.Request.Path.StartsWithSegments("/api"))
        await Results.Problem(statusCode: status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext);
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (config.GetValue<bool>("Http:RedirectToHttps"))
    app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/health", async (CancellationToken ct) =>
{
    try
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        return Results.Ok(new { status = "ok", database = "ok", timestamp = DateTimeOffset.UtcNow });
    }
    catch (MySqlException)
    {
        return Results.Problem(statusCode: 503, title: "База данных недоступна.");
    }
}).AllowAnonymous();
if (journal)
    await JournalBootstrap.Seed(config);
app.Run();
// Профиль кейса не регистрирует KPI-контроллеры, которым нужна другая схема БД.
public class ControllerProfileConvention : Microsoft.AspNetCore.Mvc.ApplicationModels.IApplicationModelConvention
{
    private readonly bool journal, legacy;
    public ControllerProfileConvention(bool journal, bool legacy)
    {
        this.journal = journal;
        this.legacy = legacy;
    }

    public void Apply(Microsoft.AspNetCore.Mvc.ApplicationModels.ApplicationModel model)
    {
        string[] journalControllers =
        {
            "Accidents",
            "Users"
        };
        for (int i = model.Controllers.Count - 1; i >= 0; i--)
        {
            string name = model.Controllers[i].ControllerName;
            if ((!journal && journalControllers.Contains(name)) || (!legacy && name != "Auth" && !journalControllers.Contains(name)))
                model.Controllers.RemoveAt(i);
        }
    }
}

public partial class Program
{
}
