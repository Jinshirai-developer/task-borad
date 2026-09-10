namespace TaskApi.Configuration;

public sealed class LegalOptions
{
    public const string CurrentTermsVersion = "2026-09-07-teams";
    public const string CurrentPrivacyVersion = "2026-09-07-teams";
    public string OperatorName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string HostingProvider { get; set; } = string.Empty;
    public string EmailProvider { get; set; } = string.Empty;
    public string LogRetention { get; set; } = string.Empty;
    public string BackupRetention { get; set; } = string.Empty;
    public bool PublicReleaseReady => !new[]
    {
        OperatorName, ContactEmail, HostingProvider, EmailProvider, LogRetention, BackupRetention
    }.Any(string.IsNullOrWhiteSpace);
}
