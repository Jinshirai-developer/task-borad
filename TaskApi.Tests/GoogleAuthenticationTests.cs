using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskApi.Configuration;
using TaskApi.Data;
using TaskApi.Models;

namespace TaskApi.Tests;

public sealed class GoogleAuthenticationTests
{
    private static TaskApiFactory Factory(GoogleBackchannel backchannel, Dictionary<string, string?>? overrides = null, TimeProvider? clock = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Google:Enabled"] = "true",
            ["Authentication:Google:ClientId"] = "integration.apps.googleusercontent.com",
            ["Authentication:Google:ClientSecret"] = "integration-only-secret"
        };
        foreach (var item in overrides ?? []) settings[item.Key] = item.Value;
        return new TaskApiFactory(settings, authenticationTimeProvider: clock, googleBackchannel: backchannel);
    }

    private static object Registration(string key = "google-user", bool accepted = true) => new
    {
        userKey = key, displayName = "Google Nickname", acceptTerms = accepted,
        termsVersion = LegalOptions.CurrentTermsVersion, privacyVersion = LegalOptions.CurrentPrivacyVersion
    };

    private static async Task<Uri> Start(HttpClient client)
    {
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/start", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Uri(json.GetProperty("url").GetString()!);
    }

    private static async Task<HttpResponseMessage> Complete(HttpClient client, Uri authorization)
    {
        var query = QueryHelpers.ParseQuery(authorization.Query);
        var callback = await client.GetAsync(QueryHelpers.AddQueryString("/signin-google", new Dictionary<string, string?>
        { ["code"] = "one-time-test-code", ["state"] = query["state"] }));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        if (callback.Headers.Location?.OriginalString != "/api/auth/google/complete") return callback;
        return await client.GetAsync(callback.Headers.Location);
    }

    private static async Task Pending(HttpClient client)
    {
        var complete = await Complete(client, await Start(client));
        Assert.Equal("/google.html", complete.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Disabled_HidesProviderAndRejectsStart_WithoutRevealingSecrets()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        var config = await client.GetFromJsonAsync<JsonElement>("/api/auth/config");
        Assert.False(config.GetProperty("googleLoginEnabled").GetBoolean());
        Assert.DoesNotContain("ClientSecret", config.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/start", new { })).StatusCode);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("register")]
    [InlineData("link")]
    [InlineData("cancel")]
    public async Task UnsafeEndpoint_RequiresCsrf(string endpoint)
    {
        using var factory = Factory(new());
        using var client = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/auth/google/{endpoint}", Registration())).StatusCode);
    }

    [Fact]
    public async Task Authorization_UsesPkceCanonicalCallbackMinimalScopes_AndDoesNotPersistTokens()
    {
        var backchannel = new GoogleBackchannel();
        using var factory = Factory(backchannel);
        using var client = factory.CreateSecureClient();
        var authorization = await Start(client);
        Assert.Equal("accounts.google.com", authorization.Host);
        var query = QueryHelpers.ParseQuery(authorization.Query);
        Assert.Equal("https://localhost/signin-google", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Equal(new[] { "email", "openid", "profile" }, query["scope"].ToString().Split(' ').Order().ToArray());
        Assert.Equal("/google.html", (await Complete(client, authorization)).Headers.Location?.OriginalString);
        Assert.Equal(query["code_challenge"], WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(backchannel.Verifier!))));
        Assert.Equal("https://localhost/signin-google", backchannel.RedirectUri);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTokens);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles);
    }

    [Fact]
    public async Task Registration_RequiresConsentThenSignsInAndUsesSubjectForLaterLogins()
    {
        var backchannel = new GoogleBackchannel();
        using var factory = Factory(backchannel);
        using var client = factory.CreateSecureClient();
        await Pending(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post,
            "/api/auth/google/register", Registration(accepted: false))).StatusCode);
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("user").GetProperty("id").GetInt32();
        Assert.Equal("ready", session.GetProperty("nextAction").GetString());
        Assert.False(session.GetProperty("hasPassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new { title = "Preserve me" })).StatusCode);
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        backchannel.Email = "changed-address@gmail.com";
        Assert.Equal("/index.html", (await Complete(client, await Start(client))).Headers.Location?.OriginalString);
        var second = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(id, second.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal("google-user@gmail.com", second.GetProperty("email").GetString());
        Assert.Contains("Preserve me", await client.GetStringAsync("/api/tasks"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.UserProfiles);
        Assert.Single(db.UserLogins);
        Assert.Empty(db.UserTokens);
        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task ExistingEmail_IsNeverAutoLinked_PasswordProofPreservesOriginalAccount()
    {
        using var factory = Factory(new() { Email = "existing@example.test" });
        using var client = factory.CreateSecureClient();
        var original = await factory.RegisterConfirmedAsync(client, "existing");
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new { title = "Existing task" });
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        await Pending(client);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/google/pending")).GetProperty("canRegister").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/link",
            new { userKey = "existing", password = "wrong-password" })).StatusCode);
        var linked = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/link", new { userKey = "existing", password = AuthTestRequests.Password });
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(original.User.Id, (await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetProperty("id").GetInt32());
        Assert.Contains("Existing task", await client.GetStringAsync("/api/tasks"));
        using var scope = factory.Services.CreateScope();
        Assert.Single(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserProfiles);
        Assert.Single(scope.ServiceProvider.GetRequiredService<AppDbContext>().UserLogins);
    }

    [Fact]
    public async Task NewSubjectWithSameEmail_CannotAccessAnExistingGoogleAccount()
    {
        var backchannel = new GoogleBackchannel();
        using var factory = Factory(backchannel);
        using var first = factory.CreateSecureClient();
        await Pending(first);
        await first.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration());
        backchannel.Subject = "different-google-subject";
        using var second = factory.CreateSecureClient();
        await Pending(second);
        Assert.Equal(HttpStatusCode.Conflict, (await second.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration("other-user"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/tasks")).StatusCode);
    }

    [Theory]
    [InlineData(false, "google-user@gmail.com", "subject")]
    [InlineData(true, "invalid-address", "subject")]
    [InlineData(true, "google-user@gmail.com", "")]
    public async Task InvalidProviderIdentity_CannotCreateLocalSession(bool verified, string email, string subject)
    {
        using var factory = Factory(new() { Verified = verified, Email = email, Subject = subject });
        using var client = factory.CreateSecureClient();
        Assert.Equal("/login.html?google=failed", (await Complete(client, await Start(client))).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData("external@example.test", "", "confirmEmail")]
    [InlineData("employee@example.test", "example.test", "ready")]
    public async Task ThirdPartyEmail_RequiresOwnConfirmationUnlessGoogleWorkspaceIsAuthoritative(string email, string domain, string next)
    {
        using var factory = Factory(new() { Email = email, Domain = domain });
        using var client = factory.CreateSecureClient();
        await Pending(client);
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(next, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextAction").GetString());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(next == "confirmEmail" ? 1 : 0, scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.Count());
    }

    [Fact]
    public async Task TamperedStateAndMissingCorrelation_AreRejectedBeforeBackchannel()
    {
        var backchannel = new GoogleBackchannel();
        using var factory = Factory(backchannel);
        using var client = factory.CreateSecureClient();
        var authorization = await Start(client);
        using var other = factory.CreateSecureClient();
        Assert.Equal("/login.html?google=failed", (await Complete(other, authorization)).Headers.Location?.OriginalString);
        var tampered = new Uri(QueryHelpers.AddQueryString("https://accounts.google.com/auth", "state", "forged"));
        Assert.Equal("/login.html?google=failed", (await Complete(client, tampered)).Headers.Location?.OriginalString);
        Assert.Equal(0, backchannel.Calls);
    }

    [Fact]
    public async Task CancelledProviderAndBackchannelFailure_ReturnToLoginWithoutDetails()
    {
        var backchannel = new GoogleBackchannel { Fail = true };
        using var factory = Factory(backchannel);
        using var client = factory.CreateSecureClient();
        var authorization = await Start(client);
        var state = QueryHelpers.ParseQuery(authorization.Query)["state"].ToString();
        var cancelled = await client.GetAsync(QueryHelpers.AddQueryString("/signin-google?error=access_denied", "state", state));
        Assert.Equal("/login.html?google=failed", cancelled.Headers.Location?.OriginalString);
        Assert.Equal("/login.html?google=failed", (await Complete(client, await Start(client))).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task ExpiredOrCancelledPendingProof_CannotRegister()
    {
        var clock = new MutableClock();
        using var factory = Factory(new(), clock: clock);
        using var client = factory.CreateSecureClient();
        await Pending(client);
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration())).StatusCode);
        await Pending(client);
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/cancel", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration())).StatusCode);
    }

    [Theory]
    [InlineData("Registration:Enabled", "false")]
    [InlineData("Registration:MaxUsers", "1")]
    public async Task RegistrationAdmission_IsEnforcedForGoogle(string key, string value)
    {
        using var factory = Factory(new(), new() { [key] = value });
        using var client = factory.CreateSecureClient();
        if (key.EndsWith("MaxUsers"))
        {
            using var existing = factory.CreateSecureClient();
            await existing.RegisterAsync("full-account");
        }
        await Pending(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration())).StatusCode);
    }

    [Fact]
    public async Task GoogleOnlyAccount_CanAcceptUpdatedTermsWithoutCreatingPassword()
    {
        using var factory = Factory(new());
        using var client = factory.CreateSecureClient();
        await Pending(client);
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserProfiles.Single().AcceptedTermsVersion = "old";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/terms", Registration(accepted: false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/terms", Registration())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task LinkedButLockedAccount_CannotSignInWithGoogle()
    {
        using var factory = Factory(new());
        using var client = factory.CreateSecureClient();
        await Pending(client);
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration());
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserProfiles.Single().LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        }
        Assert.Equal("/login.html?google=failed", (await Complete(client, await Start(client))).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tasks")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedBrowser_CannotAccidentallySwitchAccount()
    {
        using var factory = Factory(new());
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client, "original");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/start", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/link", new { userKey = "original", password = AuthTestRequests.Password })).StatusCode);
        Assert.Equal("original", (await client.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("userKey").GetString());
    }

    [Fact]
    public async Task AlternateHost_CannotBecomeTheOAuthRedirectOrigin()
    {
        using var factory = Factory(new());
        using var client = factory.CreateSecureClient();
        client.BaseAddress = new Uri("https://127.0.0.1");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/start", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/signin-google?state=forged&code=forged")).StatusCode);
    }

    [Fact]
    public async Task GoogleAccount_CanSetRecoveryPasswordAndKeepGoogleLogin()
    {
        using var factory = Factory(new());
        using var client = factory.CreateSecureClient();
        await Pending(client);
        var registered = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/google/register", Registration());
        var id = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetProperty("id").GetInt32();
        var token = await factory.GenerateTokenAsync(id, confirmation: false);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/reset-password", new { userId = id, token, password = AuthTestRequests.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(id, (await client.LoginAsync("google-user")).User.Id);
        await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/logout");
        Assert.Equal("/index.html", (await Complete(client, await Start(client))).Headers.Location?.OriginalString);
    }

    private sealed class GoogleBackchannel : HttpMessageHandler
    {
        public string Subject = "google-subject-123";
        public string Email = "google-user@gmail.com";
        public string Domain = "";
        public bool Verified = true;
        public bool Fail;
        public int Calls;
        public string? Verifier;
        public string? RedirectUri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) return new HttpResponseMessage(HttpStatusCode.BadGateway);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("oauth2.googleapis.com", request.RequestUri!.Host);
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                Verifier = form["code_verifier"];
                RedirectUri = form["redirect_uri"];
                Assert.Equal("one-time-test-code", form["code"]);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "not-a-real-token", token_type = "Bearer", expires_in = 3600 }) };
            }
            Assert.Equal("www.googleapis.com", request.RequestUri!.Host);
            Assert.Equal("not-a-real-token", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { sub = Subject, email = Email, email_verified = Verified, name = "Private Google Name", hd = Domain }) };
        }
    }
}
