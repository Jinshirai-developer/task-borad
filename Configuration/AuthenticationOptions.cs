namespace TaskApi.Configuration;

public sealed class AuthenticationOptions
{
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string KeyRingPath { get; set; } = string.Empty;
    // Entry route used by emailed links when the app is shared through an unlisted gateway.
    public string PublicEntryPath { get; set; } = "/";
}
