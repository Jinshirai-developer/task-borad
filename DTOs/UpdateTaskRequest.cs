using System.ComponentModel.DataAnnotations;
using TaskApi.Models;

namespace TaskApi.DTOs;

public class UpdateTaskRequest
{
    public int? AssigneeUserProfileId { get; set; }

    [MaxLength(20)]
    public List<ChecklistItem>? Checklist { get; set; }

    public uint? Version { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public bool IsCompleted { get; set; }

    [EnumDataType(typeof(TaskItemStatus))]
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;

    public DateTime? DueDate { get; set; }

    [EnumDataType(typeof(TaskPriority))]
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    [MaxLength(300)]
    public string? Tags { get; set; }
}
