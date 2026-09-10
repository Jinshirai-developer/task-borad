using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly TaskApiFactory _factory = new();
    private readonly HttpClient _client;

    public AuthenticationTests()
    {
        _client = _factory.CreateSecureClient();
    }

    [Fact]
    public void LegacyPasswordHash_WhenPasswordMatches_RequestsIdentityRehash()
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserProfile>>();
        var result = hasher.VerifyHashedPassword(new UserProfile(), LegacyHash(AuthTestRequests.Password),
            AuthTestRequests.Password);
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void LegacyPasswordHash_WhenPasswordDoesNotMatch_Fails()
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserProfile>>();
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(new UserProfile(), LegacyHash(AuthTestRequests.Password), "wrong"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("0.AAAA.AAAA")]
    [InlineData("-1.AAAA.AAAA")]
    [InlineData("600000.invalid!base64.invalid!base64")]
    [InlineData("600000.AAAA.AAAA.extra")]
    public void PasswordHash_WhenMalformed_FailsWithoutThrowing(string storedHash)
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserProfile>>();
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(new UserProfile(), storedHash, AuthTestRequests.Password));
    }

    [Fact]
    public void NewlyHashedPassword_UsesIdentityFormatAndValidates()
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserProfile>>();
        var user = new UserProfile();
        var hash = hasher.HashPassword(user, AuthTestRequests.Password);
        Assert.DoesNotContain('.', hash);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, hash, AuthTestRequests.Password));
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(user, hash, "wrong"));
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/forgot-password")]
    [InlineData("/api/auth/resend-confirmation")]
    [InlineData("/api/auth/confirm-email")]
    [InlineData("/api/auth/reset-password")]
    public async Task AnonymousUnsafeEndpoint_WithoutCsrf_ReturnsBadRequest(string path)
    {
        var response = await _client.PostAsJsonAsync(path, AuthTestRequests.Registration("csrf-user"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles);
    }

    [Fact]
    public async Task AuthenticatedMutation_WithoutCsrf_ReturnsBadRequest()
    {
        await _factory.RegisterConfirmedAsync(_client);
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "CSRF attempt" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CsrfToken_FromDifferentBrowser_IsRejected()
    {
        var csrf = await _client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var secondClient = _factory.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(AuthTestRequests.Registration("csrf-user"))
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await secondClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_IssuesSecureHttpOnlyCookieAndNoBearerToken()
    {
        var response = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
            AuthTestRequests.Registration("cookie-user"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, cookie =>
            cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=", StringComparison.OrdinalIgnoreCase));
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(payload.TryGetProperty("token", out _));
        Assert.Equal("confirmEmail", payload.GetProperty("nextAction").GetString());
    }

    [Fact]
    public async Task UnconfirmedAccount_IsRestrictedButCanReadSessionAndLogout()
    {
        var registration = await _client.RegisterAsync();
        Assert.False(registration.EmailConfirmed);
        Assert.False(registration.RequiresEmail);
        Assert.False(registration.RequiresTerms);
        var response = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("account_setup_required", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/session")).StatusCode);
        var logout = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        Assert.True(logout.IsSuccessStatusCode, await logout.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData(false, AuthTestRequests.PolicyVersion, "TestPassword!2026")]
    [InlineData(true, "old-version", "TestPassword!2026")]
    [InlineData(true, AuthTestRequests.PolicyVersion, "Short!123")]
    public async Task Register_WithMissingConsentStaleVersionOrShortPassword_ReturnsBadRequest(
        bool acceptTerms, string termsVersion, string password)
    {
        var response = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
            AuthTestRequests.Registration("invalid-registration", password: password,
                acceptTerms: acceptTerms, termsVersion: termsVersion));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WhenRegistrationDisabled_ReturnsServiceUnavailable()
    {
        using var factory = new TaskApiFactory(new Dictionary<string, string?>
        {
            ["Registration:Enabled"] = "false"
        });
        using var client = factory.CreateSecureClient();
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
            AuthTestRequests.Registration("disabled-user"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Register_WhenConcurrentRequestsReachLimit_AdmitsOnlyOne()
    {
        using var factory = new TaskApiFactory(new Dictionary<string, string?>
        {
            ["Registration:MaxUsers"] = "1"
        });
        var clients = Enumerable.Range(0, 8).Select(_ => factory.CreateSecureClient()).ToArray();
        try
        {
            var responses = await Task.WhenAll(clients.Select((client, index) =>
                client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
                    AuthTestRequests.Registration($"parallel-{index}"))));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.ServiceUnavailable));
            using var scope = factory.Services.CreateScope();
            Assert.Single(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task Register_WithCaseInsensitiveDuplicateEmail_IsRejected()
    {
        await _client.RegisterAsync("first-user", "Shared@Example.test");
        using var secondClient = _factory.CreateSecureClient();
        var response = await secondClient.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
            AuthTestRequests.Registration("second-user", "shared@example.test"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Single(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles);
    }

    [Fact]
    public async Task ConfirmEmail_IsSingleUseAndRevokesPreviousCookie()
    {
        var registration = await _client.RegisterAsync();
        var token = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: true);
        var payload = new { userId = registration.User.Id, token };
        var confirmation = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", payload);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);
        var repeated = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", payload);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        var session = await _client.LoginAsync(registration.User.UserKey);
        Assert.True(session.EmailConfirmed);
        Assert.Equal("ready", session.NextAction);
    }

    [Fact]
    public async Task ConfirmEmail_WithTamperedOrOtherUsersToken_DoesNotConfirm()
    {
        var first = await _client.RegisterAsync();
        using var secondClient = _factory.CreateSecureClient();
        var second = await secondClient.RegisterAsync();
        var token = await _factory.GenerateTokenAsync(first.User.Id, confirmation: true);
        var wrongUser = await secondClient.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = second.User.Id,
            token
        });
        var tampered = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = first.User.Id,
            token = token + "tampered"
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongUser.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.All(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles,
            user => Assert.False(user.EmailConfirmed));
    }

    [Theory]
    [InlineData("/api/auth/forgot-password")]
    [InlineData("/api/auth/resend-confirmation")]
    public async Task EmailRequest_DoesNotDiscloseWhetherAccountExists(string path)
    {
        var user = await _client.RegisterAsync();
        var known = await _client.SendWithCsrfAsync(HttpMethod.Post, path, new { email = user.Email });
        var missing = await _client.SendWithCsrfAsync(HttpMethod.Post, path,
            new { email = "unknown@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, missing.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResetPassword_IsSingleUseAndRevokesAllSessions()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        using var secondClient = _factory.CreateSecureClient();
        await secondClient.LoginAsync(registration.User.UserKey);
        var token = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: false);
        const string newPassword = "ChangedPassword!2026";
        var payload = new { userId = registration.User.Id, token, password = newPassword };
        var reset = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/reset-password", payload);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/api/auth/session")).StatusCode);
        var repeat = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/reset-password", payload);
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        var oldLogin = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/login", new
        {
            userKey = registration.User.UserKey,
            password = AuthTestRequests.Password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var session = await _client.LoginAsync(registration.User.UserKey, newPassword);
        Assert.Equal("ready", session.NextAction);
    }

    [Fact]
    public async Task ResetPassword_WithTamperedToken_DoesNotChangePassword()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        var response = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/reset-password", new
        {
            userId = registration.User.Id,
            token = "invalid-token",
            password = "ChangedPassword!2026"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _client.LoginAsync(registration.User.UserKey);
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_DoesNotChangePassword()
    {
        using var factory = new TaskApiFactory(resetTokenLifetime: TimeSpan.FromTicks(-1));
        using var client = factory.CreateSecureClient();
        var registration = await factory.RegisterConfirmedAsync(client);
        var token = await factory.GenerateTokenAsync(registration.User.Id, confirmation: false);
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/reset-password", new
        {
            userId = registration.User.Id,
            token,
            password = "ChangedPassword!2026"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await client.LoginAsync(registration.User.UserKey);
    }

    [Fact]
    public async Task Login_AfterFiveIncorrectPasswords_TemporarilyLocksTheAccount()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/login", new
            {
                userKey = registration.User.UserKey,
                password = "WrongPassword!2026"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }
        var correctButLocked = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/login", new
        {
            userKey = registration.User.UserKey,
            password = AuthTestRequests.Password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, correctButLocked.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserProfile>>();
        var user = Assert.IsType<UserProfile>(await manager.FindByIdAsync(registration.User.Id.ToString()));
        Assert.True(await manager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task CorrectingUnconfirmedEmail_InvalidatesOldLinkAndOtherBrowser()
    {
        var registration = await _client.RegisterAsync();
        var previousToken = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await database.UserProfiles.SingleAsync();
            user.LastConfirmationEmailAt = DateTime.UtcNow.AddMinutes(-2);
            await database.SaveChangesAsync();
        }
        using var otherBrowser = _factory.CreateSecureClient();
        await otherBrowser.LoginAsync(registration.User.UserKey);
        var complete = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/complete-registration", new
        {
            email = "corrected@example.test",
            password = AuthTestRequests.Password,
            acceptTerms = true,
            termsVersion = AuthTestRequests.PolicyVersion,
            privacyVersion = AuthTestRequests.PolicyVersion
        });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync("/api/auth/session")).StatusCode);
        var oldLink = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = registration.User.Id,
            token = previousToken
        });
        Assert.Equal(HttpStatusCode.BadRequest, oldLink.StatusCode);
        var newToken = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: true);
        var newLink = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = registration.User.Id,
            token = newToken
        });
        Assert.Equal(HttpStatusCode.OK, newLink.StatusCode);
        var session = await _client.LoginAsync(registration.User.UserKey);
        Assert.Equal("corrected@example.test", session.Email);
        Assert.True(session.EmailConfirmed);
    }

    [Fact]
    public async Task SavingUnchangedSetup_DoesNotRequeueEmailOrInvalidateExistingLink()
    {
        var registration = await _client.RegisterAsync();
        var token = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: true);
        string originalPayload;
        DateTime? originalSentAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalPayload = (await database.EmailOutbox.SingleAsync()).ProtectedPayload;
            originalSentAt = (await database.UserProfiles.SingleAsync()).LastConfirmationEmailAt;
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var complete = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/complete-registration", new
            {
                email = registration.Email,
                password = AuthTestRequests.Password,
                acceptTerms = true,
                termsVersion = AuthTestRequests.PolicyVersion,
                privacyVersion = AuthTestRequests.PolicyVersion
            });
            Assert.True(complete.StatusCode == HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(originalPayload, (await database.EmailOutbox.SingleAsync()).ProtectedPayload);
            Assert.Equal(originalSentAt, (await database.UserProfiles.SingleAsync()).LastConfirmationEmailAt);
        }
        var confirmation = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = registration.User.Id,
            token
        });
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
    }

    [Fact]
    public async Task CorrectingEmail_WithinResendCooldown_IsRateLimited()
    {
        var registration = await _client.RegisterAsync();
        var complete = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/complete-registration", new
        {
            email = "corrected@example.test",
            password = AuthTestRequests.Password,
            acceptTerms = true,
            termsVersion = AuthTestRequests.PolicyVersion,
            privacyVersion = AuthTestRequests.PolicyVersion
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, complete.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(registration.Email, (await database.UserProfiles.SingleAsync()).Email);
        Assert.Single(database.EmailOutbox);
    }

    [Fact]
    public async Task CompleteRegistration_WithWrongPassword_DoesNotChangeEmail()
    {
        var registration = await _client.RegisterAsync();
        var complete = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/complete-registration", new
        {
            email = "changed@example.test",
            password = "WrongPassword!2026",
            acceptTerms = true,
            termsVersion = AuthTestRequests.PolicyVersion,
            privacyVersion = AuthTestRequests.PolicyVersion
        });
        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles.SingleAsync();
        Assert.Equal(registration.Email, user.Email);
    }

    [Fact]
    public async Task ConfirmationEmail_IsEncryptedInOutboxAndRemovedAfterSuccessfulDelivery()
    {
        var registration = await _client.RegisterAsync();
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await database.EmailOutbox.SingleAsync();
        Assert.Equal(registration.User.Id, entry.UserProfileId);
        Assert.Equal("confirm", entry.Purpose);
        Assert.DoesNotContain(registration.Email!, entry.ProtectedPayload);
        Assert.DoesNotContain("token=", entry.ProtectedPayload);
        var sender = new TestEmailSender();
        var dispatcher = new EmailOutboxDispatcher(database,
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), sender,
            NullLogger<EmailOutboxDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchOneAsync(CancellationToken.None));
        var email = Assert.Single(sender.Delivered);
        Assert.Equal(registration.Email, email.Address);
        Assert.Contains($"https://localhost/auth.html?mode=confirm#userId={registration.User.Id}&token=", email.Text);
        Assert.Empty(database.EmailOutbox);
    }

    [Fact]
    public async Task FailedEmailDelivery_RemainsQueuedAndRetriesWithoutDuplicateRows()
    {
        await _client.RegisterAsync();
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = new TestEmailSender { Fail = true };
        var dispatcher = new EmailOutboxDispatcher(database,
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), sender,
            NullLogger<EmailOutboxDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchOneAsync(CancellationToken.None));
        var entry = await database.EmailOutbox.SingleAsync();
        Assert.Equal(1, entry.Attempts);
        Assert.True(entry.NextAttemptAt > DateTime.UtcNow);
        Assert.False(await dispatcher.DispatchOneAsync(CancellationToken.None));
        Assert.Empty(sender.Delivered);
        entry.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        await database.SaveChangesAsync();
        sender.Fail = false;
        Assert.True(await dispatcher.DispatchOneAsync(CancellationToken.None));
        Assert.Single(sender.Delivered);
        Assert.Empty(database.EmailOutbox);
    }

    [Fact]
    public async Task ExpiredEmail_IsDiscardedWithoutSending()
    {
        await _client.RegisterAsync();
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await database.EmailOutbox.SingleAsync();
        entry.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await database.SaveChangesAsync();
        var sender = new TestEmailSender();
        var dispatcher = new EmailOutboxDispatcher(database,
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), sender,
            NullLogger<EmailOutboxDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchOneAsync(CancellationToken.None));
        Assert.Empty(sender.Delivered);
        Assert.Empty(database.EmailOutbox);
    }

    [Fact]
    public async Task Logout_RevokesAllSessions()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        using var secondClient = _factory.CreateSecureClient();
        await secondClient.LoginAsync(registration.User.UserKey);
        var logout = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        Assert.True(logout.IsSuccessStatusCode, await logout.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Logout_DoesNotInvalidatePreviouslyIssuedConfirmationLink()
    {
        var registration = await _client.RegisterAsync();
        var token = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: true);
        var logout = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);

        var confirmation = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = registration.User.Id,
            token
        });
        Assert.True(confirmation.StatusCode == HttpStatusCode.OK,
            await confirmation.Content.ReadAsStringAsync());
        var session = await _client.LoginAsync(registration.User.UserKey);
        Assert.True(session.EmailConfirmed);
        Assert.Equal("ready", session.NextAction);
    }

    [Fact]
    public async Task Logout_DoesNotInvalidatePreviouslyIssuedPasswordResetLink()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        var token = await _factory.GenerateTokenAsync(registration.User.Id, confirmation: false);
        var logout = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);

        const string newPassword = "ChangedAfterLogout!2026";
        var reset = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/reset-password", new
        {
            userId = registration.User.Id,
            token,
            password = newPassword
        });
        Assert.True(reset.StatusCode == HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        var session = await _client.LoginAsync(registration.User.UserKey, newPassword);
        Assert.Equal("ready", session.NextAction);
    }

    [Fact]
    public async Task Cookie_ExpiresAfterEightHoursEvenWhenUsedDuringThatPeriod()
    {
        var clock = new ManualAuthenticationClock();
        using var factory = new TaskApiFactory(authenticationTimeProvider: clock);
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);
        clock.Advance(TimeSpan.FromHours(7));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task LegacyUser_LoginRehashesPasswordAndCompletesEmailSetupWithoutLosingTasks()
    {
        const string userKey = "legacy-user";
        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new UserProfile
            {
                UserKey = userKey,
                UserName = userKey,
                NormalizedUserName = userKey.ToUpperInvariant(),
                DisplayName = "Legacy User",
                PasswordHash = LegacyHash(AuthTestRequests.Password),
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            database.UserProfiles.Add(user);
            await database.SaveChangesAsync();
            userId = user.Id;
            database.Tasks.Add(new TaskItem { UserProfileId = userId, Title = "Preserved legacy task" });
            await database.SaveChangesAsync();
        }
        var login = await _client.LoginAsync(userKey);
        Assert.Equal(userId, login.User.Id);
        Assert.True(login.RequiresEmail);
        Assert.True(login.RequiresTerms);
        Assert.Equal("addEmail", login.NextAction);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/tasks")).StatusCode);
        var complete = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/complete-registration", new
        {
            email = "legacy@example.test",
            password = AuthTestRequests.Password,
            acceptTerms = true,
            termsVersion = AuthTestRequests.PolicyVersion,
            privacyVersion = AuthTestRequests.PolicyVersion
        });
        Assert.True(complete.StatusCode == HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        var setup = Assert.IsType<AuthPayload>(await complete.Content.ReadFromJsonAsync<AuthPayload>());
        Assert.Equal("confirmEmail", setup.NextAction);
        var token = await _factory.GenerateTokenAsync(userId, confirmation: true);
        var confirm = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email",
            new { userId, token });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await _client.LoginAsync(userKey);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/tasks")).StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedUser = await verification.UserProfiles.SingleAsync();
        Assert.Equal(userId, savedUser.Id);
        Assert.DoesNotContain('.', savedUser.PasswordHash!);
        Assert.Equal("Preserved legacy task", (await verification.Tasks.SingleAsync()).Title);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string LegacyHash(string password)
    {
        const int iterations = 600_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private sealed class TestEmailSender : ITransactionalEmailSender
    {
        public bool Fail { get; set; }
        public List<TransactionalEmail> Delivered { get; } = [];

        public Task SendAsync(TransactionalEmail email, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Simulated SMTP delivery failure.");
            Delivered.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualAuthenticationClock : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
