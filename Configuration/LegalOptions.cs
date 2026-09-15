namespace TaskApi.Configuration;

public sealed class LegalOptions
{
    public const string CurrentTermsVersion = "2026-09-07-teams";
    public const string CurrentPrivacyVersion = "2026-09-07-teams";
    // Keep the actual operator identity private; publish only the service/contact label.
    public string OperatorName { get; set; } = string.Empty;
    public string OperatorDisplayName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string HostingProvider { get; set; } = string.Empty;
    public string EmailProvider { get; set; } = string.Empty;
    public string LogRetention { get; set; } = string.Empty;
    public string BackupRetention { get; set; } = string.Empty;
    public bool PublicReleaseReady => !new[]
    {
        OperatorName, OperatorDisplayName, ContactEmail, HostingProvider, EmailProvider, LogRetention, BackupRetention
    }.Any(string.IsNullOrWhiteSpace);
}
