using TaskApi.Models;

namespace TaskApi.DTOs;

public class TaskResponse
{
    public int Id { get; set; }

    public int? TeamId { get; set; }

    public int? AssigneeUserProfileId { get; set; }
    public string? AssigneeDisplayName { get; set; }
    public List<ChecklistItem> Checklist { get; set; } = [];
    public bool NeedsHelp { get; set; }
    public bool HandoffPending { get; set; }
    public int? HandoffRecipientId { get; set; }
    public int NoteCount { get; set; }
    public UndoReceipt? Undo { get; set; }

    public int? CreatedByUserProfileId { get; set; }

    public string CreatedByDisplayName { get; set; } = string.Empty;

    public uint Version { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsCompleted { get; set; }

    public TaskItemStatus Status { get; set; }

    public DateTime? DueDate { get; set; }

    public TaskPriority Priority { get; set; }

    public string? Tags { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
