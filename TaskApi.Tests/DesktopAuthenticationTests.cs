using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskApi.Data;

namespace TaskApi.Tests;

public sealed class DesktopAuthenticationTests
{
    private sealed record Challenge(string DeviceCode, string UserCode, string VerificationUri);
    private static async Task<Challenge> Start(HttpClient client)
    {
        var response = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/desktop/start", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Challenge>())!;
    }
    private static Task<HttpResponseMessage> Exchange(HttpClient client, Challenge challenge) =>
        client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/desktop/exchange", new { challenge.DeviceCode });
    private static Task<HttpResponseMessage> Approve(HttpClient client, Challenge challenge, bool approve = true) =>
        client.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/desktop/approve", new { challenge.UserCode, approve });

    [Fact]
    public async Task BrowserApproval_CreatesHttpOnlySessionOnce_WithoutReturningCookieOrProviderTokensInJson()
    {
        using var factory = new TaskApiFactory();
        using var browser = factory.CreateSecureClient();
        using var desktop = factory.CreateSecureClient();
        var account = await factory.RegisterConfirmedAsync(browser);
        var challenge = await Start(desktop);
        Assert.Equal(43, challenge.DeviceCode.Length);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", challenge.UserCode);
        Assert.Equal("https://localhost/desktop.html#code=" + challenge.UserCode, challenge.VerificationUri);
        Assert.Equal(HttpStatusCode.Accepted, (await Exchange(desktop, challenge)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Approve(browser, challenge)).StatusCode);
        var exchange = await Exchange(desktop, challenge);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        Assert.Contains(exchange.Headers.GetValues("Set-Cookie"), header => header.StartsWith("__Host-TaskBoard.Auth=")
            && header.Contains("httponly", StringComparison.OrdinalIgnoreCase) && header.Contains("secure", StringComparison.OrdinalIgnoreCase));
        var body = await exchange.Content.ReadAsStringAsync();
        Assert.DoesNotContain(challenge.DeviceCode, body);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        var session = await desktop.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(account.User.Id, session.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.Gone, (await Exchange(desktop, challenge)).StatusCode);
    }

    [Fact]
    public async Task AllUnsafeEndpoints_RequireCsrf_AndApprovalRequiresAReadyAccount()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/desktop/start", new { })).StatusCode);
        var challenge = await Start(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/desktop/exchange", new { challenge.DeviceCode })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Approve(client, challenge)).StatusCode);
        await client.RegisterAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Approve(client, challenge)).StatusCode);
    }

    [Fact]
    public async Task UnknownDeviceCodeAndDeniedRequest_CannotSignIn()
    {
        using var factory = new TaskApiFactory();
        using var browser = factory.CreateSecureClient();
        using var desktop = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(browser);
        var challenge = await Start(desktop);
        Assert.Equal(HttpStatusCode.Gone, (await Exchange(desktop, challenge with { DeviceCode = new string('A', 43) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Approve(browser, challenge, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Exchange(desktop, challenge)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Approve(browser, challenge)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await desktop.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task DatabaseContainsOnlyHashes_AndExpiredCodesCannotBeApproved()
    {
        using var factory = new TaskApiFactory();
        using var browser = factory.CreateSecureClient();
        using var desktop = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(browser);
        var challenge = await Start(desktop);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.DesktopSignIns.SingleAsync();
            Assert.Equal(64, entry.DeviceCodeHash.Length);
            Assert.Equal(64, entry.UserCodeHash.Length);
            Assert.DoesNotContain(challenge.DeviceCode, JsonSerializer.Serialize(entry));
            Assert.DoesNotContain(challenge.UserCode.Replace("-", ""), JsonSerializer.Serialize(entry));
            entry.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Gone, (await Approve(browser, challenge)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Exchange(desktop, challenge)).StatusCode);
    }

    [Fact]
    public async Task SessionRevocationAfterApproval_PreventsExchange()
    {
        using var factory = new TaskApiFactory();
        using var browser = factory.CreateSecureClient();
        using var desktop = factory.CreateSecureClient();
        var account = await factory.RegisterConfirmedAsync(browser);
        var challenge = await Start(desktop);
        Assert.Equal(HttpStatusCode.NoContent, (await Approve(browser, challenge)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.UserProfiles.SingleAsync(item => item.Id == account.User.Id);
            user.SessionVersion = Guid.NewGuid().ToString();
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Gone, (await Exchange(desktop, challenge)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentExchanges_ConsumeApprovalExactlyOnce()
    {
        using var factory = new TaskApiFactory();
        using var browser = factory.CreateSecureClient();
        using var desktop = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(browser);
        var challenge = await Start(desktop);
        Assert.Equal(HttpStatusCode.NoContent, (await Approve(browser, challenge)).StatusCode);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var contender = factory.CreateSecureClient();
            using var response = await Exchange(contender, challenge);
            return response.StatusCode;
        }));
        Assert.Equal(1, results.Count(code => code == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(code => code == HttpStatusCode.Gone));
    }
}
