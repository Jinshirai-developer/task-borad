using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskApi.Models;
using TaskApi.Services;
using TaskApi.DTOs;
using TaskApi.Authentication;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/teams/{teamId:int}/tasks")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TasksController : ControllerBase
{
    private readonly TaskService _taskService;

    public TasksController(TaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet]
    public IActionResult Get([FromQuery]bool? isCompleted, [FromQuery] TaskItemStatus? status, [FromQuery] TaskPriority? priority, [FromQuery] string? tag, [FromQuery] string? sortOrder, [FromQuery]string? search,[FromQuery]int page = 1, [FromQuery]int pageSize = 10, [FromRoute] int? teamId = null, [FromQuery] string? tagExact = null, [FromQuery] bool untagged = false, [FromQuery] string? assignee = null, [FromQuery] string? due = null)
    {
        // Model binding turns an explicitly empty string into null. Preserve the
        // distinction between an omitted exact filter and an invalid empty one.
        var hasExactTag = Request.Query.ContainsKey("tagExact");
        if (hasExactTag && untagged)
            return BadRequest(new { code = "invalid_tag_filters", message = "tagExactとuntaggedは同時に指定できません。" });
        if (hasExactTag && string.IsNullOrWhiteSpace(tagExact))
            return BadRequest(new { code = "invalid_exact_tag", message = "tagExactは1〜300文字のタグ名を指定してください。" });

        if (page < 1)
        {
            return BadRequest(new { message = "pageは1以上を指定してください。" });
        }

        if (pageSize < 1)
        {
            return BadRequest(new { message = "pageSizeは1以上を指定してください。" });
        }

        if (pageSize > 100)
        {
            return BadRequest(new { message = "pageSizeは100以下を指定してください。" });
        }

        if (page > 10_000)
        {
            return BadRequest(new { message = "pageは10000以下を指定してください。" });
        }

        if (search?.Length > 200)
        {
            return BadRequest(new { message = "searchは200文字以下を指定してください。" });
        }

        if (tag?.Length > 100)
        {
            return BadRequest(new { message = "tagは100文字以下を指定してください。" });
        }

        if (!string.IsNullOrWhiteSpace(sortOrder)
            && sortOrder is not "asc" and not "desc" and not "due")
        {
            return BadRequest(new { message = "sortOrderはasc、desc、dueを指定してください。" });
        }

        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            return BadRequest(new { message = "statusの値が不正です。" });
        }

        if (priority.HasValue && !Enum.IsDefined(priority.Value))
        {
            return BadRequest(new { message = "priorityの値が不正です。" });
        }

        try
        {
            var tasks = _taskService.GetAll(GetUserId(), isCompleted, status, priority, tag, sortOrder, search, page, pageSize, teamId, tagExact, untagged, assignee, due);
            return Ok(tasks);
        }
        catch (TeamOperationException error) { return TeamError(error); }
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateTaskRequest task, [FromRoute] int? teamId = null)
    {
        try
        {
            var createdTask = _taskService.Create(GetUserId(), task, teamId);
            var prefix = teamId.HasValue ? $"/api/teams/{teamId.Value}/tasks" : "/api/tasks";
            return Created($"{prefix}/{createdTask.Id}", createdTask);
        }
        catch (TeamOperationException error) { return TeamError(error); }
        catch (TaskLimitExceededException error)
        {
            return Problem(
                title: "Task limit reached",
                detail: error.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return Problem(
                title: "Task could not be saved",
                detail: "データが同時に更新されました。再読み込みしてからもう一度お試しください。",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id, [FromRoute] int? teamId = null)
    {
        TaskResponse? task;
        try { task = _taskService.GetById(GetUserId(), id, teamId); }
        catch (TeamOperationException error) { return TeamError(error); }

        if (task == null)
        {
            return NotFound();
        }

        return Ok(task);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] UpdateTaskRequest request, [FromRoute] int? teamId = null)
    {
        TaskResponse? task;

        try
        {
            task = _taskService.Update(GetUserId(), id, request, teamId);
        }
        catch (TeamOperationException error) { return TeamError(error); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                title: "Task update conflict",
                detail: "別の操作でタスクが更新されました。再読み込みしてからもう一度お試しください。",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (task == null)
        {
            return NotFound();
        }

        return Ok(task);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id, [FromQuery] uint? version = null, [FromRoute] int? teamId = null)
    {
        UndoReceipt? receipt;

        try
        {
            receipt = _taskService.DeleteWithUndo(GetUserId(), id, version, teamId);
        }
        catch (TeamOperationException error) { return TeamError(error); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                title: "Task delete conflict",
                detail: "別の操作でタスクが更新されました。再読み込みしてからもう一度お試しください。",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (receipt == null)
        {
            return NotFound();
        }
        Response.Headers["X-Task-Undo"] = receipt.Token.ToString();
        Response.Headers["X-Task-Undo-Expires"] = receipt.ExpiresAt.ToString("O");
        return NoContent();
    }

    [HttpPost("undo/{token:guid}")]
    public IActionResult Undo(Guid token, [FromRoute] int? teamId = null)
    {
        try { return Ok(_taskService.Undo(GetUserId(), token, teamId)); }
        catch (TeamOperationException error) { return TeamError(error); }
        catch (TaskLimitExceededException error) { return Conflict(new { message = error.Message }); }
        catch (DbUpdateException) { return Conflict(new { code = "undo_conflict", message = "別の操作と競合しました。最新の状態を確認してください。" }); }
    }

    private int GetUserId() => RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id;

    private IActionResult TeamError(TeamOperationException error) =>
        StatusCode(error.StatusCode, new { code = error.Code, message = error.Message });
}
