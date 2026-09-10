using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaskApi.Authentication;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi;

[ApiController, Route("api/auth/desktop")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DesktopAuthController(DesktopSignInService desktop, SignInManager<UserProfile> signIn,
    IOptions<Configuration.AuthenticationOptions> settings) : ControllerBase
{
    [HttpPost("start"), AllowAnonymous, EnableRateLimiting("auth")]
    public IActionResult Start() => Run(() =>
    {
        var request = desktop.Start();
        return Ok(new
        {
            request.DeviceCode, request.UserCode, expiresIn = DesktopSignInService.LifetimeSeconds, interval = 3,
            verificationUri = settings.Value.PublicBaseUrl.TrimEnd('/') + "/desktop.html#code=" + request.UserCode
        });
    });

    [HttpPost("approve"), EnableRateLimiting("auth")]
    public IActionResult Approve(DesktopDecision request) => Run(() =>
    {
        desktop.Decide(request.UserCode, RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id, request.Approve);
        return NoContent();
    });

    // Polling uses the global limiter, not the 10/minute password-attempt policy.
    // All POSTs, including native-client calls, require the existing CSRF token + cookie.
    [HttpPost("exchange"), AllowAnonymous]
    public async Task<IActionResult> Exchange(DesktopExchange request)
    {
        try
        {
            var user = desktop.Consume(request.DeviceCode);
            if (user == null) return Accepted(new { status = "pending" });
            await signIn.SignInAsync(user, isPersistent: false, authenticationMethod: "DesktopBrowser");
            return Ok(AuthenticationService.ToResponse(user));
        }
        catch (AccountOperationException error) { return Problem(statusCode: error.StatusCode, detail: error.Message); }
    }

    private IActionResult Run(Func<IActionResult> action)
    {
        try { return action(); }
        catch (AccountOperationException error) { return Problem(statusCode: error.StatusCode, detail: error.Message); }
    }
}

public sealed record DesktopDecision([Required, StringLength(12)] string UserCode, bool Approve);
public sealed record DesktopExchange([Required, StringLength(43, MinimumLength = 43)] string DeviceCode);
