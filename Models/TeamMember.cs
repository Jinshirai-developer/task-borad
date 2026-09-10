namespace TaskApi.Models;

public sealed class TeamMember
{
    public int TeamId { get; set; }
    public int UserProfileId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
