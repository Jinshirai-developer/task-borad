namespace TaskApi.Models;

// One current reward receipt per existing task. Deleted tasks retain their receipt,
// while deleted accounts leave no receiver that could accidentally be recreated.
public sealed class CompletionReward
{
    public int Id { get; set; }
    public int? TaskId { get; set; }
    public TaskItem? Task { get; set; }
    public int? UserProfileId { get; set; }
    public DateTime AwardedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int Experience { get; set; } = 25;
    public int EnergyGranted { get; set; }
}
