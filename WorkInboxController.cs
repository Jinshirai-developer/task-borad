using Microsoft.AspNetCore.Mvc;
using TaskApi.Authentication;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/work-inbox")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkInboxController(TaskService tasks) : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] string view = "incoming")
    {
        try { return Ok(tasks.GetWorkInbox(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id, view)); }
        catch (TeamOperationException error) { return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message }); }
    }
}
