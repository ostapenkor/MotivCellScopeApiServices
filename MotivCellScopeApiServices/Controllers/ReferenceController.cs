using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MotivCellScopeApiServices.Models;

namespace MotivCellScopeApiServices.Controllers;
[ApiController]
[Authorize(Roles = "engineer,operator,admin")]
[Route("api/reference")]
public class ReferenceController : ControllerBase
{
    private readonly MotivReportDbContext db;
    public ReferenceController(MotivReportDbContext db)
    {
        this.db = db;
    }

    // Все списки для формы одной HTTP-командой. Id остаются настоящими FK новой БД.
    [HttpGet("all")]
    public IActionResult All()
    {
        return Ok(new { regions = db.Regions.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.RegionId, x.Code, x.Name, x.TzOffsetMin }).ToList(), directions = db.Directions.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.DirectionId, x.Code, x.Name, x.Kind }).ToList(), technicalCenters = db.TechnicalCenters.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.TechCenterId, x.DirectionId, x.Code, x.Name }).ToList(), priorities = db.IncidentPriorities.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Select(x => new { x.PriorityId, x.Code, x.Name, x.SortOrder }).ToList(), networkTypes = db.NetworkTypes.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.NetworkTypeId, x.Code, x.Name }).ToList(), equipmentTypes = db.EquipmentTypes.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.EquipmentTypeId, x.Code, x.Name }).ToList(), alarmTypes = db.AlarmTypes.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.AlarmTypeId, x.Code, x.Name }).ToList(), categories = new[] { new Choice("power", "Электроснабжение"), new Choice("transport", "Транспорт"), new Choice("hardware", "Оборудование"), new Choice("software", "ПО"), new Choice("planned", "Плановые работы"), new Choice("client", "Обращение клиента"), new Choice("external", "Внешнее"), new Choice("other", "Прочее") }, severities = new[] { new Choice("low", "Низкая"), new Choice("medium", "Средняя"), new Choice("high", "Высокая"), new Choice("critical", "Критическая") } });
    }

    [HttpGet("/api/regions")]
    public IActionResult Regions() => Ok(db.Regions.AsNoTracking().OrderBy(x => x.RegionId).Select(x => new { x.RegionId, x.Code, x.Name, x.TzOffsetMin }).ToList());
    // Коллега удалил vendors в новой схеме. URL оставлен с явным объяснением,
    // вместо выдумывания поставщиков по подстрокам названий оборудования.
    [HttpGet("/api/vendors")]
    public IActionResult Vendors() => StatusCode(410, new { error = "vendors_removed", message = "В актуальной схеме main таблица vendors удалена. Типы оборудования доступны в /api/reference/all." });
    [AllowAnonymous]
    [HttpGet("/api/health")]
    public IActionResult Health()
    {
        try
        {
            using var connection = new MySqlConnector.MySqlConnection(db.Database.GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
            return Ok(new { status = "ok", database = "ok", version = "1.1.0", timestamp = DateTime.UtcNow });
        }
        catch (DbException)
        {
            return StatusCode(503, new { status = "degraded", database = "unavailable", timestamp = DateTime.UtcNow });
        }
    }

    public record Choice(string Code, string Label);
    // Синхронный фильтр исключений: ошибки SQL остаются в журнале сервера.
    // DTO и вспомогательный код находятся рядом с использующим их контроллером.
    public class ApiExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<ApiExceptionFilter> logger;
        public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
        {
            this.logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            logger.LogError(context.Exception, "API request failed: {Trace}", context.HttpContext.TraceIdentifier);
            context.Result = new ObjectResult(new { error = "server_error", message = "Не удалось выполнить запрос. Подробности находятся в журнале API.", traceId = context.HttpContext.TraceIdentifier })
            {
                StatusCode = 500
            };
            context.ExceptionHandled = true;
        }
    }
}
