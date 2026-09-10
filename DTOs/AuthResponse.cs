namespace TaskApi.DTOs;

public class AuthResponse
{
    public UserProfileResponse User { get; set; } = new();

    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool HasPassword { get; set; }
    public bool RequiresEmail { get; set; }
    public bool RequiresTerms { get; set; }
    public string NextAction { get; set; } = "ready";
}
