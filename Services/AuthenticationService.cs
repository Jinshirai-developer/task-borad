using System.Data;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TaskApi.Configuration;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public sealed partial class AuthenticationService(
    AppDbContext database,
    UserManager<UserProfile> users,
    SignInManager<UserProfile> signIn,
    EmailOutboxService outbox,
    IOptions<RegistrationOptions> registrationOptions,
    ILogger<AuthenticationService> logger)
{
    private static readonly SemaphoreSlim RegistrationAdmission = new(1, 1);
    private const long RegistrationLockKey = 2_026_090_701;
    private const string InvalidCredentials = "ユーザーIDまたはパスワードが違うか、一時的にログインが制限されています。";
    private const string InvalidLink = "リンクが無効、有効期限切れ、または使用済みです。メールを再送してやり直してください。";

    public async Task<AuthResponse> RegisterAsync(RegisterUserRequest request)
    {
        var options = registrationOptions.Value;
        if (!options.Enabled)
            throw new AccountOperationException(503, "現在、新規登録を停止しています。");
        ValidateConsent(request.AcceptTerms, request.TermsVersion, request.PrivacyVersion);
        var key = UserProfileService.NormalizeUserKey(request.UserKey);
        if (key == "guest" || key.Length is < 3 or > 100
            || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new AccountOperationException(400, "ユーザーIDは半角英数字・ハイフン・アンダースコアで指定してください。");

        var user = new UserProfile
        {
            UserKey = key, UserName = key, Email = request.Email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? key : request.DisplayName.Trim(),
            LockoutEnabled = true
        };
        AcceptTerms(user);
        await RegistrationAdmission.WaitAsync();
        try
        {
            await using var transaction = await BeginTransactionAsync();
            if (transaction != null)
                await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({RegistrationLockKey})");
            if (await database.UserProfiles.CountAsync() >= options.MaxUsers)
                throw new AccountOperationException(503, "デモ環境の登録上限に達しています。");
            if (await users.FindByEmailAsync(user.Email) != null || await users.FindByNameAsync(key) != null)
                throw new AccountOperationException(409, "この登録情報は使用できません。ログインまたはパスワード再設定をお試しください。");
            EnsureSuccess(await users.CreateAsync(user, request.Password));
            await QueueConfirmationAsync(user);
            if (transaction != null) await transaction.CommitAsync();
        }
        finally { RegistrationAdmission.Release(); }

        await signIn.SignInAsync(user, isPersistent: false);
        logger.LogInformation("Account registered. UserId: {UserId}", user.Id);
        return ToResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginUserRequest request)
    {
        var user = await users.FindByNameAsync(UserProfileService.NormalizeUserKey(request.UserKey));
        if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            logger.LogWarning("Login rejected.");
            throw new AccountOperationException(401, InvalidCredentials);
        }
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning("Login rejected.");
            throw new AccountOperationException(401, InvalidCredentials);
        }
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        EnsureSuccess(await users.UpdateAsync(user));
        await signIn.SignInAsync(user, isPersistent: false);
        logger.LogInformation("Login succeeded. UserId: {UserId}", user.Id);
        return ToResponse(user);
    }

    public async Task<AuthResponse> CompleteRegistrationAsync(UserProfile user, CompleteRegistrationRequest request)
    {
        ValidateConsent(request.AcceptTerms, request.TermsVersion, request.PrivacyVersion);
        if (!(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            throw new AccountOperationException(400, "パスワードを確認して、しばらく待ってから再度お試しください。");
        var email = request.Email.Trim();
        var emailChanged = users.NormalizeEmail(user.Email) != users.NormalizeEmail(email);
        if (emailChanged && user.EmailConfirmed)
            throw new AccountOperationException(400, "確認済みメールアドレスの変更には対応していません。");
        var existing = await users.FindByEmailAsync(email);
        if (existing != null && existing.Id != user.Id)
            throw new AccountOperationException(409, "このメールアドレスは使用できません。");
        if (!emailChanged && !RequiresTerms(user)) return ToResponse(user);
        if (emailChanged && user.LastConfirmationEmailAt > DateTime.UtcNow.AddMinutes(-1))
            throw new AccountOperationException(429, "メールアドレスの変更は確認メール送信から1分以上待ってお試しください。");

        await using var transaction = await BeginTransactionAsync();
        if (emailChanged)
        {
            user.Email = email;
            user.EmailConfirmed = false;
            user.LastConfirmationEmailAt = null;
            user.LastResetEmailAt = null;
            user.SecurityStamp = Guid.NewGuid().ToString();
        }
        AcceptTerms(user);
        // Revoke old cookies; changing only legal consent must not invalidate an email link.
        user.SessionVersion = Guid.NewGuid().ToString();
        EnsureSuccess(await users.UpdateAsync(user));
        if (emailChanged) await outbox.RemoveForUserAsync(user.Id);
        if (!user.EmailConfirmed) await QueueConfirmationAsync(user);
        if (transaction != null) await transaction.CommitAsync();
        await signIn.SignInAsync(user, isPersistent: false);
        return ToResponse(user);
    }

    public async Task RequestConfirmationAsync(string email)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is { EmailConfirmed: false })
            await QueueConfirmationAsync(user);
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is not { EmailConfirmed: true } || user.LastResetEmailAt > DateTime.UtcNow.AddMinutes(-1)) return;
        await using var transaction = await BeginTransactionAsync();
        user.LastResetEmailAt = DateTime.UtcNow;
        if (!(await users.UpdateAsync(user)).Succeeded) return;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await outbox.QueueAsync(user, "reset", Encode(token), TimeSpan.FromMinutes(30));
        if (transaction != null) await transaction.CommitAsync();
    }

    public async Task ConfirmEmailAsync(EmailTokenRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        var token = Decode(request.Token);
        if (user == null || user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email) || token == null)
            throw new AccountOperationException(400, InvalidLink);
        await using var transaction = await BeginTransactionAsync();
        var result = await users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded) throw new AccountOperationException(400, InvalidLink);
        EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        await outbox.RemoveForUserAsync(user.Id);
        if (transaction != null) await transaction.CommitAsync();
        await signIn.SignOutAsync();
        logger.LogInformation("Email confirmed. UserId: {UserId}", user.Id);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        var token = Decode(request.Token);
        if (user == null || !user.EmailConfirmed || token == null)
            throw new AccountOperationException(400, InvalidLink);
        await using var transaction = await BeginTransactionAsync();
        var result = await users.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded) throw new AccountOperationException(400, InvalidLink);
        // ResetPasswordAsync rotates the security stamp, invalidating every existing cookie/token.
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.UpdatedAt = DateTime.UtcNow;
        EnsureSuccess(await users.UpdateAsync(user));
        await outbox.RemoveForUserAsync(user.Id);
        if (transaction != null) await transaction.CommitAsync();
        await signIn.SignOutAsync();
        logger.LogInformation("Password reset completed. UserId: {UserId}", user.Id);
    }

    public async Task LogoutAsync(UserProfile user)
    {
        // Revoke all browser sessions without invalidating confirmation/recovery emails already sent.
        user.SessionVersion = Guid.NewGuid().ToString();
        EnsureSuccess(await users.UpdateAsync(user));
        await signIn.SignOutAsync();
        logger.LogInformation("All sessions logged out. UserId: {UserId}", user.Id);
    }

    public static bool RequiresTerms(UserProfile user) =>
        user.AcceptedTermsVersion != LegalOptions.CurrentTermsVersion
        || user.AcknowledgedPrivacyVersion != LegalOptions.CurrentPrivacyVersion;

    public static AuthResponse ToResponse(UserProfile user)
    {
        var needsEmail = string.IsNullOrWhiteSpace(user.Email);
        var needsTerms = RequiresTerms(user);
        return new AuthResponse
        {
            User = UserProfileService.ToResponse(user), Email = user.Email,
            EmailConfirmed = user.EmailConfirmed, RequiresEmail = needsEmail, RequiresTerms = needsTerms,
            HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
            NextAction = needsEmail ? "addEmail" : needsTerms ? "acceptTerms" : !user.EmailConfirmed ? "confirmEmail" : "ready"
        };
    }

    private async Task QueueConfirmationAsync(UserProfile user)
    {
        if (user.LastConfirmationEmailAt > DateTime.UtcNow.AddMinutes(-1)) return;
        // Caller may already own a transaction during registration/setup.
        await using var transaction = database.Database.CurrentTransaction == null
            ? await BeginTransactionAsync() : null;
        user.LastConfirmationEmailAt = DateTime.UtcNow;
        EnsureSuccess(await users.UpdateAsync(user));
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await outbox.QueueAsync(user, "confirm", Encode(token), TimeSpan.FromHours(24));
        if (transaction != null) await transaction.CommitAsync();
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync() => database.Database.IsRelational()
        ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted) : null;

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string? Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return null; }
    }

    private static void ValidateConsent(bool accepted, string terms, string privacy)
    {
        if (!accepted || terms != LegalOptions.CurrentTermsVersion || privacy != LegalOptions.CurrentPrivacyVersion)
            throw new AccountOperationException(400, "現在の利用規約に同意し、プライバシーポリシーを確認してください。");
    }

    private static void AcceptTerms(UserProfile user)
    {
        user.AcceptedTermsVersion = LegalOptions.CurrentTermsVersion;
        user.AcknowledgedPrivacyVersion = LegalOptions.CurrentPrivacyVersion;
        user.TermsAcceptedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(error => error.Code.Contains("Password", StringComparison.Ordinal)))
            throw new AccountOperationException(400, "パスワードは12文字以上100文字以内で指定してください。");
        throw new AccountOperationException(409, "登録情報が使用できないか、別の処理で更新されました。画面を更新してお試しください。");
    }
}
