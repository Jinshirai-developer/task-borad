using Microsoft.AspNetCore.Mvc;
using TaskApi.Authentication;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController, Route("api/pet/weekly-review")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WeeklyReviewController(WeeklyReviewService service) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        try { return Ok(service.Get(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id)); }
        catch (TeamOperationException error) { return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message }); }
    }
}
