using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/task-tags")]
[Route("api/teams/{teamId:int}/task-tags")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TaskTagsController(TaskTagService service) : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromRoute] int? teamId = null)
    {
        try { return Ok(service.GetAll(UserId, teamId)); }
        catch (TeamOperationException error) { return TagError(error); }
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateTaskTagRequest request, [FromRoute] int? teamId = null)
    {
        try
        {
            var result = service.Create(UserId, request, teamId);
            var location = teamId.HasValue ? $"/api/teams/{teamId.Value}/task-tags" : "/api/task-tags";
            return Created(location, result);
        }
        catch (TeamOperationException error) { return TagError(error); }
        catch (DbUpdateException)
        {
            return Conflict(new { code = "tag_conflict", message = "分類タグが別の操作で更新されました。再読み込みしてやり直してください。" });
        }
    }

    private int UserId => RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id;
    [HttpPost("manage")]
    public IActionResult Manage([FromBody] ManageTaskTagRequest request, [FromRoute] int? teamId = null)
    {
        try { return Ok(service.Manage(UserId, request, teamId)); }
        catch (TeamOperationException error) { return TagError(error); }
        catch (DbUpdateException) { return Conflict(new { code = "tag_conflict", message = "別の操作で変更されています。一覧を更新してください。" }); }
    }

    private IActionResult TagError(TeamOperationException error) =>
        StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
}
