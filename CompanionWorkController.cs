using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/companion")]
[Route("api/teams/{teamId:int}/companion")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CompanionWorkController(TaskService tasks) : ControllerBase
{
    private int UserId => RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id;
    private IActionResult Run(Func<object> action)
    {
        try { return Ok(action()); }
        catch (TeamOperationException error) { return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message }); }
        catch (DbUpdateException) { return Conflict(new { code = "work_conflict", message = "別の操作と競合しました。最新の記録を読み直してください。" }); }
    }
    [HttpGet] public IActionResult Dashboard([FromRoute] int? teamId = null) => Run(() => tasks.GetCompanionDashboard(UserId, teamId));
    [HttpGet("notes")] public IActionResult Notes([FromQuery] string? q, [FromQuery] string? tags, [FromQuery] int? excludeTaskId, [FromRoute] int? teamId = null)
        => Run(() => tasks.FindCompanionNotes(UserId, q, tags, excludeTaskId, teamId));
    [HttpGet("tasks/{taskId:int}")] public IActionResult Get(int taskId, [FromRoute] int? teamId = null)
        => Run(() => tasks.GetCompanionWork(UserId, taskId, teamId));
    [HttpPost("tasks/{taskId:int}")] public IActionResult Change(int taskId, [FromBody] CompanionWorkRequest request, [FromRoute] int? teamId = null)
        => Run(() => tasks.ChangeCompanionWork(UserId, taskId, request, teamId));
}
