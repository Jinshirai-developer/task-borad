namespace TaskApi.Configuration;

public sealed class PublicProxyOptions
{
    // Optional gateway authentication. Leave empty when no public gateway is used.
    public string Secret { get; set; } = string.Empty;
}
