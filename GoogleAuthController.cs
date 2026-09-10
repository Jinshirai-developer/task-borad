using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using TaskApi.Authentication;
using TaskApi.Configuration;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;
using AuthenticationService = TaskApi.Services.AuthenticationService;

namespace TaskApi;

[ApiController, Route("api/auth/google")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class GoogleAuthController(AuthenticationService authentication, SignInManager<UserProfile> signIn,
    UserManager<UserProfile> users, IOptions<GoogleLoginOptions> google, IOptions<RegistrationOptions> registration,
    IOptions<Configuration.AuthenticationOptions> settings) : ControllerBase
{
    [HttpPost("start"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Start()
    {
        if (!google.Value.Enabled) return Problem(statusCode: 503, detail: "Googleログインは現在利用できません。");
        if (User.Identity?.IsAuthenticated == true) return Problem(statusCode: 409, detail: "ログアウトしてからGoogleログインをお試しください。");
        if (!GoogleAuthentication.MatchesOrigin(Request, settings.Value.PublicBaseUrl))
            return Problem(statusCode: 400, detail: "アプリの正式なURLからログインしてください。");
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = signIn.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, "/api/auth/google/complete");
        properties.Items["prompt"] = "select_account";
        await HttpContext.ChallengeAsync(GoogleDefaults.AuthenticationScheme, properties);
        // Initiate navigation in the browser after a CSRF-protected POST, avoiding cross-origin fetch redirects.
        var url = Response.Headers.Location.ToString();
        Response.Headers.Remove("Location");
        Response.StatusCode = 200;
        return Ok(new { url });
    }

    [HttpGet("complete"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Complete()
    {
        try
        {
            var info = await PendingAsync();
            var session = await authentication.LoginGoogleAsync(info);
            if (session == null) return LocalRedirect("/google.html");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect(session.NextAction == "ready" ? "/index.html" : "/auth.html");
        }
        catch (AccountOperationException)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect(GoogleAuthentication.FailurePath);
        }
    }

    [HttpGet("pending"), AllowAnonymous]
    public Task<IActionResult> Pending() => RunAsync(async () =>
    {
        var info = await PendingAsync();
        var email = AuthenticationService.GoogleEmail(info);
        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "";
        return Ok(new { email, displayName = displayName[..Math.Min(displayName.Length, 100)],
            canRegister = registration.Value.Enabled && await users.FindByEmailAsync(email) == null });
    });

    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> Register(GoogleRegistrationRequest request) => FinishAsync(info => authentication.RegisterGoogleAsync(info, request));

    [HttpPost("link"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> Link(LoginUserRequest request) => FinishAsync(info => authentication.LinkGoogleAsync(info, request));

    [HttpPost("terms"), AllowAccountSetup, EnableRateLimiting("auth")]
    public Task<IActionResult> Terms(ConsentRequest request) => RunAsync(async () =>
        Ok(await authentication.AcceptGoogleTermsAsync(RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext), request)));

    [HttpPost("cancel"), AllowAnonymous]
    public async Task<IActionResult> Cancel()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return NoContent();
    }

    private Task<IActionResult> FinishAsync(Func<ExternalLoginInfo, Task<AuthResponse>> action) => RunAsync(async () =>
    {
        var session = await action(await PendingAsync());
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Ok(session);
    });

    private async Task<ExternalLoginInfo> PendingAsync()
    {
        if (!google.Value.Enabled || User.Identity?.IsAuthenticated == true)
            throw new AccountOperationException(409, "Googleログインを最初からやり直してください。");
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info == null) throw new AccountOperationException(401, "Googleの確認期限が切れました。ログイン画面からやり直してください。");
        AuthenticationService.GoogleEmail(info);
        return info;
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AccountOperationException error) { return Problem(statusCode: error.StatusCode, detail: error.Message); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Problem(statusCode: 409, detail: "この登録情報は使用できません。ログイン画面からやり直してください。"); }
    }
}
