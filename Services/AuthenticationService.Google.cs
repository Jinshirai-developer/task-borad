using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public sealed partial class AuthenticationService
{
    public static string GoogleEmail(ExternalLoginInfo info)
    {
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (info.LoginProvider != GoogleDefaults.AuthenticationScheme || string.IsNullOrWhiteSpace(info.ProviderKey)
            || info.ProviderKey.Length > 255 || email == null || email.Length > 254
            || !new EmailAddressAttribute().IsValid(email)
            || !string.Equals(info.Principal.FindFirstValue(GoogleAuthentication.VerifiedEmailClaim), "true", StringComparison.OrdinalIgnoreCase))
            throw new AccountOperationException(400, "Googleアカウントのメールアドレスを確認できませんでした。別のアカウントでお試しください。");
        return email;
    }

    public async Task<AuthResponse?> LoginGoogleAsync(ExternalLoginInfo info)
    {
        GoogleEmail(info);
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user == null) return null;
        return await SignInGoogleAsync(user);
    }

    public async Task<AuthResponse> RegisterGoogleAsync(ExternalLoginInfo info, GoogleRegistrationRequest request)
    {
        var email = GoogleEmail(info);
        if (!registrationOptions.Value.Enabled)
            throw new AccountOperationException(503, "現在、新規登録を停止しています。登録済みの方はアカウントの連携をご利用ください。");
        ValidateConsent(request.AcceptTerms, request.TermsVersion, request.PrivacyVersion);
        var key = UserProfileService.NormalizeUserKey(request.UserKey);
        if (key == "guest" || key.Length is < 3 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new AccountOperationException(400, "ユーザーIDは3〜100文字の半角英数字・ハイフン・アンダースコアで指定してください。");
        var domain = email[(email.LastIndexOf('@') + 1)..];
        // Google is not authoritative for an ordinary third-party email, even if it was verified previously.
        var confirmed = domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase)
            || domain.Equals(info.Principal.FindFirstValue("urn:google:hd"), StringComparison.OrdinalIgnoreCase);
        var user = new UserProfile
        {
            UserKey = key, UserName = key, Email = email, EmailConfirmed = confirmed, LockoutEnabled = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? key : request.DisplayName.Trim()
        };
        AcceptTerms(user);
        await RegistrationAdmission.WaitAsync();
        try
        {
            await using var transaction = await BeginTransactionAsync();
            if (transaction != null)
                await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({RegistrationLockKey})");
            if (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) != null)
                throw new AccountOperationException(409, "このGoogleアカウントは連携済みです。ログイン画面からやり直してください。");
            if (await users.FindByEmailAsync(email) != null || await users.FindByNameAsync(key) != null)
                throw new AccountOperationException(409, "この登録情報は使用できません。登録済みの方はアカウントの連携をご利用ください。");
            if (await database.UserProfiles.CountAsync() >= registrationOptions.Value.MaxUsers)
                throw new AccountOperationException(503, "デモ環境の登録上限に達しています。");
            EnsureSuccess(await users.CreateAsync(user));
            EnsureSuccess(await users.AddLoginAsync(user, info));
            if (!confirmed) await QueueConfirmationAsync(user);
            if (transaction != null) await transaction.CommitAsync();
        }
        finally { RegistrationAdmission.Release(); }
        return await SignInGoogleAsync(user);
    }

    public async Task<AuthResponse> LinkGoogleAsync(ExternalLoginInfo info, LoginUserRequest request)
    {
        GoogleEmail(info);
        var user = await users.FindByNameAsync(UserProfileService.NormalizeUserKey(request.UserKey));
        if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash)
            || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            throw new AccountOperationException(401, InvalidCredentials);
        await RegistrationAdmission.WaitAsync();
        try
        {
            await using var transaction = await BeginTransactionAsync();
            if (transaction != null)
                await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({RegistrationLockKey})");
            if (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) != null
                || (await users.GetLoginsAsync(user)).Any(login => login.LoginProvider == info.LoginProvider))
                throw new AccountOperationException(409, "このアカウントにはGoogle連携が設定されています。連携済みのGoogleアカウントでログインしてください。");
            EnsureSuccess(await users.AddLoginAsync(user, info));
            user.SessionVersion = Guid.NewGuid().ToString();
            EnsureSuccess(await users.UpdateAsync(user));
            if (transaction != null) await transaction.CommitAsync();
        }
        finally { RegistrationAdmission.Release(); }
        // Linking never changes or confirms the existing account's email address.
        return await SignInGoogleAsync(user);
    }

    public async Task<AuthResponse> AcceptGoogleTermsAsync(UserProfile user, ConsentRequest request)
    {
        if (!string.IsNullOrEmpty(user.PasswordHash) || !(await users.GetLoginsAsync(user)).Any(login => login.LoginProvider == GoogleDefaults.AuthenticationScheme))
            throw new AccountOperationException(400, "アカウントの手続き画面からやり直してください。");
        ValidateConsent(request.AcceptTerms, request.TermsVersion, request.PrivacyVersion);
        AcceptTerms(user);
        EnsureSuccess(await users.UpdateAsync(user));
        return ToResponse(user);
    }

    private async Task<AuthResponse> SignInGoogleAsync(UserProfile user)
    {
        if (await users.IsLockedOutAsync(user)) throw new AccountOperationException(401, InvalidCredentials);
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        EnsureSuccess(await users.UpdateAsync(user));
        await signIn.SignInAsync(user, isPersistent: false, authenticationMethod: GoogleDefaults.AuthenticationScheme);
        return ToResponse(user);
    }
}
