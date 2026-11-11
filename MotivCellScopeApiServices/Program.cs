using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;

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

// 4. Склеиваем всё в одну полноценную строку подключения программно
var finalConnectionString = $"{baseConnectionString}user={dbUser};password={dbPassword};";

// 5. Регистрируем контекст базы данных
builder.Services.AddDbContext<MotivReportDbContext>(options =>
    options.UseMySql(
        finalConnectionString,
        ServerVersion.AutoDetect(finalConnectionString),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()
    ));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
