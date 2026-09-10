using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaskApi.Data;
using TaskApi.Models;

namespace TaskApi.Tests;

public sealed class ApiIntegrationTests : IDisposable
{
    private readonly TaskApiFactory _factory = new();
    private readonly HttpClient _client;

    public ApiIntegrationTests()
    {
        _client = _factory.CreateSecureClient();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutCookie_ReturnsUnauthorizedWithoutRedirect()
    {
        var response = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithLegacyBearerHeader_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "old-or-forged-token");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_Confirm_CreateAndListTask_CompletesAuthenticatedFlow()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        Assert.True(registration.User.Id > 0);
        var createResponse = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new
        {
            title = "HTTP integration task",
            description = "Created through the real controller pipeline",
            status = "Todo",
            priority = "High",
            tags = "integration, api"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var listResponse = await _client.GetAsync("/api/tasks?page=1&pageSize=10");
        var page = await listResponse.Content.ReadFromJsonAsync<TaskPagePayload>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("HTTP integration task", Assert.Single(page.Items).Title);
    }

    [Fact]
    public async Task SeparateCookies_KeepTasksIsolatedIncludingMutation()
    {
        await _factory.RegisterConfirmedAsync(_client);
        var createResponse = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new
        {
            title = "Private task"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = Assert.IsType<TaskWithId>(await createResponse.Content.ReadFromJsonAsync<TaskWithId>());
        using var secondClient = _factory.CreateSecureClient();
        await _factory.RegisterConfirmedAsync(secondClient);
        var page = await secondClient.GetFromJsonAsync<TaskPagePayload>("/api/tasks");
        Assert.Empty(Assert.IsType<TaskPagePayload>(page).Items);
        var response = await secondClient.SendWithCsrfAsync(HttpMethod.Delete, $"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var ownerPage = await _client.GetFromJsonAsync<TaskPagePayload>("/api/tasks");
        Assert.Single(Assert.IsType<TaskPagePayload>(ownerPage).Items);
    }

    [Fact]
    public async Task Register_WithInvalidUserKey_ReturnsBadRequest()
    {
        var response = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
            AuthTestRequests.Registration("invalid user!"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTask_WithOversizedDescription_ReturnsBadRequest()
    {
        await _factory.RegisterConfirmedAsync(_client);
        var response = await _client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new
        {
            title = "Oversized task",
            description = new string('x', 2001)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginPage_IsIncludedInApplicationContent()
    {
        var response = await _client.GetAsync("/login.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("/terms.html")]
    [InlineData("/privacy.html")]
    public async Task PolicyPage_IsAvailableWithoutAuthentication(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ReadinessEndpoint_WhenDatabaseIsAvailable_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCurrentUser_InvalidatesCookiesInEveryBrowser()
    {
        var registration = await _factory.RegisterConfirmedAsync(_client);
        using var secondClient = _factory.CreateSecureClient();
        await secondClient.LoginAsync(registration.User.UserKey);
        var deleteResponse = await _client.SendWithCsrfAsync(HttpMethod.Delete, "/api/user");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/api/auth/session")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.UserProfiles.AnyAsync(user => user.Id == registration.User.Id));
    }

    [Fact]
    public async Task UnconfirmedAccount_CanDeleteItself()
    {
        await _client.RegisterAsync();
        var response = await _client.SendWithCsrfAsync(HttpMethod.Delete, "/api/user");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/session")).StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record TaskPagePayload(List<TaskPayload> Items, int TotalCount);
    private sealed record TaskPayload(string Title);
    private sealed record TaskWithId(int Id);
}

public sealed class TaskApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"task-api-integration-{Guid.NewGuid():N}";
    private readonly Dictionary<string, string?> _settings;
    private readonly TimeSpan? _resetTokenLifetime;
    private readonly TimeProvider? _authenticationTimeProvider;
    private readonly TaskApi.Services.IStripeTestGateway? _billingGateway;
    private readonly HttpMessageHandler? _googleBackchannel;

    public TaskApiFactory(Dictionary<string, string?>? settings = null, TimeSpan? resetTokenLifetime = null,
        TimeProvider? authenticationTimeProvider = null, TaskApi.Services.IStripeTestGateway? billingGateway = null,
        HttpMessageHandler? googleBackchannel = null)
    {
        _settings = settings ?? new Dictionary<string, string?>();
        _resetTokenLifetime = resetTokenLifetime;
        _authenticationTimeProvider = authenticationTimeProvider;
        _billingGateway = billingGateway;
        _googleBackchannel = googleBackchannel;
    }

    public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true
    });

    public async Task<AuthPayload> RegisterConfirmedAsync(HttpClient client, string? userKey = null)
    {
        var registration = await client.RegisterAsync(userKey);
        var token = await GenerateTokenAsync(registration.User.Id, confirmation: true);
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/confirm-email", new
        {
            userId = registration.User.Id,
            token
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var session = await client.LoginAsync(registration.User.UserKey);
        Assert.True(session.EmailConfirmed);
        Assert.Equal("ready", session.NextAction);
        return session;
    }

    public async Task<string> GenerateTokenAsync(int userId, bool confirmation)
    {
        using var scope = Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<UserProfile>>();
        var user = Assert.IsType<UserProfile>(await manager.FindByIdAsync(userId.ToString()));
        var token = confirmation
            ? await manager.GenerateEmailConfirmationTokenAsync(user)
            : await manager.GeneratePasswordResetTokenAsync(user);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=integration;Username=integration;Password=integration",
                ["Authentication:PublicBaseUrl"] = "https://localhost",
                ["Authentication:KeyRingPath"] = string.Empty,
                ["Email:Host"] = "smtp.example.test",
                ["Email:Port"] = "1025",
                ["Email:Security"] = "None",
                ["Email:FromAddress"] = "noreply@example.test"
            };
            foreach (var setting in _settings)
            {
                settings[setting.Key] = setting.Value;
            }
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            if (_billingGateway != null) services.AddSingleton(_billingGateway);
            if (_googleBackchannel != null) services.PostConfigure<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>("Google",
                options => options.Backchannel = new HttpClient(_googleBackchannel));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            if (_resetTokenLifetime is { } lifetime)
            {
                services.PostConfigure<DataProtectionTokenProviderOptions>(options =>
                    options.TokenLifespan = lifetime);
            }
            if (_authenticationTimeProvider is { } clock)
            {
                services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
                    options => options.TimeProvider = clock);
                services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
                services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme,
                    options => options.TimeProvider = clock);
                services.PostConfigure<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>("Google",
                    options => options.TimeProvider = clock);
            }
            // HTTP tests inspect the durable outbox without contacting an SMTP server.
            foreach (var registration in services.Where(service =>
                         service.ServiceType == typeof(IHostedService)
                         && service.ImplementationType?.Name is "EmailOutboxWorker" or "BillingReconciliationWorker").ToArray())
            {
                services.Remove(registration);
            }
        });
    }
}

internal static class AuthTestRequests
{
    public const string Password = "TestPassword!2026";
    public const string PolicyVersion = TaskApi.Configuration.LegalOptions.CurrentTermsVersion;

    public static object Registration(string userKey, string? email = null, string? password = null,
        bool acceptTerms = true, string? termsVersion = null) => new
    {
        userKey,
        displayName = "Integration User",
        email = email ?? $"{userKey}@example.test",
        password = password ?? Password,
        acceptTerms,
        termsVersion = termsVersion ?? PolicyVersion,
        privacyVersion = PolicyVersion
    };

    public static async Task<HttpResponseMessage> SendWithCsrfAsync(this HttpClient client,
        HttpMethod method, string path, object? body = null)
    {
        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.True(csrfResponse.IsSuccessStatusCode, await csrfResponse.Content.ReadAsStringAsync());
        var csrf = Assert.IsType<CsrfPayload>(await csrfResponse.Content.ReadFromJsonAsync<CsrfPayload>());
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await client.SendAsync(request);
    }

    public static async Task<AuthPayload> RegisterAsync(this HttpClient client, string? userKey = null,
        string? email = null)
    {
        userKey ??= $"user-{Guid.NewGuid():N}";
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register",
            Registration(userKey, email));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        return Assert.IsType<AuthPayload>(await response.Content.ReadFromJsonAsync<AuthPayload>());
    }

    public static async Task<AuthPayload> LoginAsync(this HttpClient client, string userKey,
        string? password = null)
    {
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/login", new
        {
            userKey,
            password = password ?? Password
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return Assert.IsType<AuthPayload>(await response.Content.ReadFromJsonAsync<AuthPayload>());
    }

    private sealed record CsrfPayload(string Token);
}

public sealed record AuthPayload(UserPayload User, string? Email, bool EmailConfirmed,
    bool RequiresEmail, bool RequiresTerms, string NextAction);
public sealed record UserPayload(int Id, string UserKey, string DisplayName);
