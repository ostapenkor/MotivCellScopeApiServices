using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotivCellScopeApiServices.Contracts;
using MotivCellScopeApiServices.Data;

namespace MotivCellScopeApiServices.Controllers;
[ApiController, Authorize(Roles = "admin"), Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AccidentStore store;
    public UsersController(AccidentStore store) => this.store = store;
    private int Actor => int.Parse(User.FindFirstValue("uid")!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await store.Users(ct));
    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var user = await store.User(id, ct);
        return user == null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateJournalUser input, CancellationToken ct)
    {
        if (!JournalOptions.Roles.Contains(input.Role))
            ModelState.AddModelError("role", "Неизвестная роль.");
        if (Encoding.UTF8.GetByteCount(input.Password) > 72)
            ModelState.AddModelError("password", "BCrypt принимает не более 72 байт UTF-8.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        int id = await store.CreateUser(input, Actor, ct);
        if (id == -1)
            return Conflict(new ProblemDetails { Title = "Логин уже занят.", Status = 409 });
        return CreatedAtAction(nameof(Get), new { id }, await store.User(id, ct));
    }

    [HttpPut("{id:int:min(1)}/access")]
    public async Task<IActionResult> Access(int id, UpdateJournalAccess input, CancellationToken ct)
    {
        if (!JournalOptions.Roles.Contains(input.Role))
        {
            ModelState.AddModelError("role", "Неизвестная роль.");
            return ValidationProblem(ModelState);
        }

        if (id == Actor && (!input.Active || input.Role != "admin"))
            return Conflict(new ProblemDetails { Title = "Нельзя отключить или понизить права своей текущей учётной записи.", Status = 409 });
        int result = await store.UpdateAccess(id, input, Actor, ct);
        if (result == 0)
            return NotFound();
        if (result == -1)
            return Conflict(new ProblemDetails { Title = "Права пользователя уже изменены. Обновите список.", Status = 409 });
        return Ok(await store.User(id, ct));
    }
}
