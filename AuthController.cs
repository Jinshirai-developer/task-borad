using Microsoft.AspNetCore.Antiforgery;
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

namespace TaskApi.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class AuthController(
    AuthenticationService authentication, IAntiforgery antiforgery,
    IOptions<RegistrationOptions> registration, IOptions<LegalOptions> legal,
    IOptions<GoogleLoginOptions> google) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpGet("config"), AllowAnonymous]
    public IActionResult Config() => Ok(new
    {
        registrationEnabled = registration.Value.Enabled,
        googleLoginEnabled = google.Value.Enabled,
        termsVersion = LegalOptions.CurrentTermsVersion,
        privacyVersion = LegalOptions.CurrentPrivacyVersion,
        operatorName = legal.Value.OperatorDisplayName,
        legal.Value.ContactEmail,
        legal.Value.HostingProvider, legal.Value.EmailProvider,
        legal.Value.LogRetention, legal.Value.BackupRetention, legal.Value.PublicReleaseReady
    });

    [HttpGet("session"), AllowAccountSetup]
    public IActionResult Session() => Ok(AuthenticationService.ToResponse(CurrentUser()));

    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> Register([FromBody] RegisterUserRequest request) =>
        RunAsync(async () => Ok(await authentication.RegisterAsync(request)));

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> Login([FromBody] LoginUserRequest request) =>
        RunAsync(async () => Ok(await authentication.LoginAsync(request)));

    [HttpPost("complete-registration"), AllowAccountSetup, EnableRateLimiting("auth")]
    public Task<IActionResult> CompleteRegistration([FromBody] CompleteRegistrationRequest request) =>
        RunAsync(async () => Ok(await authentication.CompleteRegistrationAsync(CurrentUser(), request)));

    [HttpPost("resend-confirmation"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> ResendConfirmation([FromBody] EmailRequest request) => RunAsync(async () =>
    {
        try { await authentication.RequestConfirmationAsync(request.Email); }
        catch (AccountOperationException error) when (error.StatusCode == 409) { }
        return Accepted(new { message = "登録情報が一致し、確認が必要な場合にメールを送信します。届かない場合は迷惑メールをご確認のうえ、1分以上待って再送してください。" });
    });

    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> ForgotPassword([FromBody] EmailRequest request) => RunAsync(async () =>
    {
        await authentication.RequestPasswordResetAsync(request.Email);
        return Accepted(new { message = "登録済みで確認済みのメールアドレスの場合に再設定メールを送信します。届かない場合は迷惑メールをご確認のうえ、1分以上待って再送してください。" });
    });

    [HttpPost("confirm-email"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> ConfirmEmail([FromBody] EmailTokenRequest request) => RunAsync(async () =>
    {
        await authentication.ConfirmEmailAsync(request);
        return Ok(new { message = "メールアドレスを確認しました。ログインしてください。" });
    });

    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request) => RunAsync(async () =>
    {
        await authentication.ResetPasswordAsync(request);
        return Ok(new { message = "パスワードを再設定しました。新しいパスワードでログインしてください。" });
    });

    [HttpPost("logout"), AllowAccountSetup]
    public Task<IActionResult> Logout() => RunAsync(async () =>
    {
        await authentication.LogoutAsync(CurrentUser());
        return NoContent();
    });

    private UserProfile CurrentUser() => RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext);

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AccountOperationException error)
        {
            return Problem(title: "Account operation failed", detail: error.Message, statusCode: error.StatusCode);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(title: "Account conflict", detail: "この登録情報は使用できません。画面を更新してお試しください。", statusCode: 409);
        }
    }
}
