namespace TaskApi.Models;

public sealed class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerUserProfileId { get; set; }
    public string InviteCodeHash { get; set; } = string.Empty;
    public DateTime InviteExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public uint Version { get; set; }
}
