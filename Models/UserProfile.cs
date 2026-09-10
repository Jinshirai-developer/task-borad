using Microsoft.AspNetCore.Identity;

namespace TaskApi.Models;

public class UserProfile : IdentityUser<int>
{
    public string UserKey { get; set; } = "guest";

    public string DisplayName { get; set; } = "Guest";

    public string Theme { get; set; } = "classic";

    public string Layout { get; set; } = "board";

    public string? AcceptedTermsVersion { get; set; }

    public string? AcknowledgedPrivacyVersion { get; set; }

    public DateTime? TermsAcceptedAt { get; set; }

    public DateTime? LastConfirmationEmailAt { get; set; }

    public DateTime? LastResetEmailAt { get; set; }

    public string SessionVersion { get; set; } = Guid.NewGuid().ToString();

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
