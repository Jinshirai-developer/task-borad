using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskApi.Data;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class PublicProxyTests
{
    private const string Secret = "gateway-test-key-32-characters-long";

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-key")]
    public async Task DirectTunnelRequests_CannotReadPagesOrCallApis(string? key)
    {
        using var factory = new TaskApiFactory(settings: new() { ["PublicProxy:Secret"] = Secret });
        using var client = factory.CreateSecureClient();
        if (key != null) client.DefaultRequestHeaders.Add("X-TaskBoard-Proxy-Key", key);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-TaskBoard-Client-IP", "203.0.113.10");
        foreach (var path in new[] { "/login.html", "/api/auth/config", "/health/ready" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/register",
            AuthTestRequests.Registration("untrusted"))).StatusCode);
    }

    [Fact]
    public async Task Gateway_PreservesCsrfAndEmitsEntryLinkWithFragmentToken()
    {
        using var factory = new TaskApiFactory(settings: new()
        {
            ["PublicProxy:Secret"] = Secret,
            ["Authentication:PublicEntryPath"] = "/p/review-link/"
        });
        using var client = factory.CreateSecureClient();
        client.DefaultRequestHeaders.Add("X-TaskBoard-Proxy-Key", Secret);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register",
            AuthTestRequests.Registration("without-csrf"))).StatusCode);
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/register", AuthTestRequests.Registration("gateway-user"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queued = await db.EmailOutbox.SingleAsync();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(EmailOutboxService.ProtectionPurpose);
        var email = JsonSerializer.Deserialize<TransactionalEmail>(protector.Unprotect(queued.ProtectedPayload))!;
        Assert.Contains("https://localhost/p/review-link/auth.html?mode=confirm#userId=", email.Text);
        Assert.DoesNotContain("?token=", email.Text);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("confirmEmail", session.GetProperty("nextAction").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tasks")).StatusCode);
    }
}
