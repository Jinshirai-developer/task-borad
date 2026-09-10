namespace TaskApi.Models;

public sealed class DesktopSignInRequest
{
    public string DeviceCodeHash { get; set; } = "";
    public string UserCodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int? UserProfileId { get; set; }
    public string? SessionVersion { get; set; }
    public bool Denied { get; set; }
}
