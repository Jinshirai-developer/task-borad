namespace TaskApi.Configuration;

// This application intentionally has no live-mode switch.
public sealed class BillingOptions
{
    public bool Enabled { get; set; }
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PriceId { get; set; } = "";
    public int MonthlyYen { get; set; } = 500;
    public bool IsConfigured => Enabled && IsTestKey(SecretKey) && WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal)
        && PriceId.StartsWith("price_", StringComparison.Ordinal);
    public static bool IsTestKey(string value) => value.StartsWith("sk_test_", StringComparison.Ordinal)
        || value.StartsWith("rk_test_", StringComparison.Ordinal);
    public bool IsValid() => MonthlyYen is >= 50 and <= 100000
        && (string.IsNullOrEmpty(SecretKey) || IsTestKey(SecretKey)) && (!Enabled || IsConfigured);
}
