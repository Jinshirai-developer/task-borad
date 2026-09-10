namespace TaskApi.Models;

// Short-lived, actor-scoped undo capability. No new reward is granted on restore.
public sealed class TaskUndoEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserProfileId { get; set; }
    public int? TeamId { get; set; }
    public int TaskId { get; set; }
    public bool WasDeleted { get; set; }
    public string Snapshot { get; set; } = string.Empty;
    public int? RewardId { get; set; }
    public uint ExpectedVersion { get; set; }
    public DateTime ExpectedUpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
