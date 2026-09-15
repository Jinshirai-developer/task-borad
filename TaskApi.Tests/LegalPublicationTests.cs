using System.Net;
using System.Text.Json;

namespace TaskApi.Tests;

public sealed class LegalPublicationTests
{
    private static Dictionary<string, string?> Settings(string displayName) => new()
    {
        ["Legal:OperatorName"] = "INTERNAL_TEST_OPERATOR_IDENTITY",
        ["Legal:OperatorDisplayName"] = displayName,
        ["Legal:ContactEmail"] = "support@example.test",
        ["Legal:HostingProvider"] = "Test hosting",
        ["Legal:EmailProvider"] = "Test email",
        ["Legal:LogRetention"] = "Test retention",
        ["Legal:BackupRetention"] = "Test backup retention",
        ["Email:Username"] = "private-smtp-login@example.test"
    };

    [Fact]
    public async Task AnonymousConfig_ReturnsPublicLabelWithoutInternalOperatorOrSmtpIdentity()
    {
        using var factory = new TaskApiFactory(Settings("Task Board（個人運営）"));
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var config = document.RootElement;

        Assert.Equal("Task Board（個人運営）", config.GetProperty("operatorName").GetString());
        Assert.Equal("support@example.test", config.GetProperty("contactEmail").GetString());
        Assert.True(config.GetProperty("publicReleaseReady").GetBoolean());
        Assert.DoesNotContain("INTERNAL_TEST_OPERATOR_IDENTITY", body);
        Assert.DoesNotContain("private-smtp-login@example.test", body);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingPublicLabel_DoesNotFallBackToInternalIdentity(string displayName)
    {
        using var factory = new TaskApiFactory(Settings(displayName));
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.False(document.RootElement.GetProperty("publicReleaseReady").GetBoolean());
        Assert.True(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("operatorName").GetString()));
        Assert.DoesNotContain("INTERNAL_TEST_OPERATOR_IDENTITY", body);
    }
}
