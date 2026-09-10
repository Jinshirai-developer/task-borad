namespace TaskApi.Models;

public class TaskItem
{
    public int Id { get; set; }

    public int? UserProfileId { get; set; }

    public int? TeamId { get; set; }

    public int? AssigneeUserProfileId { get; set; }

    public string ChecklistJson { get; set; } = "[]";

    public string CompanionJson { get; set; } = "{}";

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsCompleted { get; set; }

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;

    public DateTime? DueDate { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public string? Tags { get; set; }

    public DateTime? CompletionRewardedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public uint Version { get; set; }
}
