namespace TaskApi.Configuration;

public sealed class GoogleLoginOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool IsValid() => !Enabled || (ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(ClientSecret));
}
