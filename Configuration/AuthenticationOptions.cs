namespace TaskApi.Configuration;

public sealed class AuthenticationOptions
{
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string KeyRingPath { get; set; } = string.Empty;
}
