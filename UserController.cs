using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskApi.Models;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly UserProfileService _userProfileService;

    public UserController(UserProfileService userProfileService)
    {
        _userProfileService = userProfileService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var user = RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext);

        return Execute(() => _userProfileService.GetProfile(user.Id));
    }

    [HttpPut]
    public IActionResult Update([FromBody] UpdateUserProfileRequest request)
    {
        var user = RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext);

        return Execute(() => _userProfileService.UpdateProfile(user.Id, request));
    }

    [HttpGet("preferences")]
    public IActionResult GetPreferences() => Execute(() => _userProfileService.GetPreferences(
        RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id));

    [HttpPut("preferences")]
    public IActionResult UpdatePreferences([FromBody] UpdateUserPreferencesRequest request) => Execute(() =>
        _userProfileService.UpdatePreferences(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id, request));

    [HttpGet("unlocks")]
    public IActionResult GetUnlocks() => Execute(() => _userProfileService.GetUnlocks(
        RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id));

    private IActionResult Execute<T>(Func<T> operation)
    {
        try { return Ok(operation()); }
        catch (TeamOperationException exception)
        {
            return StatusCode(exception.StatusCode, new { code = exception.Code, message = exception.Message });
        }
        catch (DbUpdateConcurrencyException) { return ProfileConflict(); }
    }

    private IActionResult ProfileConflict() => Conflict(new
    {
        code = "profile_conflict", message = "別の操作でアカウントが更新されました。再読み込みしてからもう一度お試しください。"
    });

    [HttpDelete]
    [AllowAccountSetup]
    public async Task<IActionResult> Delete([FromServices] SignInManager<UserProfile> signIn)
    {
        var user = RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext);
        try { _userProfileService.DeleteProfile(user.Id); }
        catch (TeamOperationException exception)
        {
            return StatusCode(exception.StatusCode, new { code = exception.Code, message = exception.Message });
        }
        catch (DbUpdateConcurrencyException) { return ProfileConflict(); }
        await signIn.SignOutAsync();

        return NoContent();
    }
}
