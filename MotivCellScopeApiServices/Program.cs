using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MotivCellScopeApiServices.Models;
using MotivCellScopeApiServices.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Подключаем чтение Docker Secrets из папки /run/secrets/
// Файлы db_user и db_password превратятся в builder.Configuration["db_user"] и builder.Configuration["db_password"]
if (Directory.Exists("/run/secrets"))
{
    builder.Configuration.AddKeyPerFile(directoryPath: "/run/secrets", optional: true);
}

// 2. Читаем безопасный шаблон строки подключения из appsettings.json
var baseConnectionString = builder.Configuration.GetConnectionString("MotivReportConnection")
    ?? throw new InvalidOperationException("Базовая строка подключения не найдена.");

// 3. Достаем логин и пароль напрямую из Docker Secrets (внутри памяти контейнера)
var dbUser = builder.Configuration["db_user"] ?? "admin"; // admin - значение по умолчанию
var dbPassword = builder.Configuration["db_password"];
var jwtKey = builder.Configuration["jwt_key"];

// 4. Склеиваем всё в одну полноценную строку подключения программно
var finalConnectionString = $"{baseConnectionString}user={dbUser};password={dbPassword};";

// 3. Кладем прочитанный ключ в память конфигурации, чтобы AuthController и JwtBearer его увидели
builder.Configuration["Jwt:Key"] = jwtKey;

// 5. Регистрируем контекст базы данных
builder.Services.AddDbContext<MotivReportDbContext>(options =>
    options.UseMySql(
        finalConnectionString,
        ServerVersion.AutoDetect(finalConnectionString),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()
    ));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

//код который позволяет в swagger, авторизоваться и вставить токен
builder.Services.AddSwaggerGen(options =>
{
    // 1. Описываем схему безопасности (кнопку Authorize в интерфейсе)
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Вставьте сюда ваш JWT-токен, полученный из /api/auth/login"
    });

    // 2. Указываем Swagger, что этот токен нужно прикреплять к защищенным запросам
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});


//настройка аутентификации
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });


builder.Services.AddHttpContextAccessor(); // <-- Обязательно, чтобы сервис видел текущий HTTP-запрос и IP
builder.Services.AddScoped<AuditService>();  // <-- Регистрируем наш единый сервис аудита

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
