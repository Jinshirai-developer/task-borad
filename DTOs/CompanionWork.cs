using System.ComponentModel.DataAnnotations;
using TaskApi.Models;

namespace TaskApi.DTOs;

// Stored inside the task so deletion/Undo restore the complete work context.
// Never return this object directly: Savepoints contains private per-user notes.
public sealed class CompanionWorkState
{
    public List<WorkSavepoint> Savepoints { get; set; } = [];
    public WorkHelp? Help { get; set; }
    public List<WorkHelp> Thanks { get; set; } = [];
    public WorkHandoff? Handoff { get; set; }
    public List<WorkNote> Notes { get; set; } = [];
    public WorkShowcase? Showcase { get; set; }
}

public sealed class WorkSavepoint
{
    public int UserId { get; set; }
    public string Summary { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string? ResourceUrl { get; set; }
    public DateTime SavedAt { get; set; }
}

public sealed class WorkHelp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? AuthorId { get; set; }
    // Null on legacy/all-team requests. Unavailable distinguishes a removed
    // recipient from a deliberately public request after account anonymization.
    public int? RecipientId { get; set; }
    public bool RecipientUnavailable { get; set; }
    public int? HelperId { get; set; }
    public string Kind { get; set; } = "review";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "open";
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class WorkHandoff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? FromUserId { get; set; }
    public int? ToUserId { get; set; }
    public bool AssignOnAccept { get; set; }
    public string Request { get; set; } = "";
    public string Criteria { get; set; } = "";
    public string? ResourceUrl { get; set; }
    public string Status { get; set; } = "sent";
    public string? Reply { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}

public sealed class WorkNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? AuthorId { get; set; }
    public string Tried { get; set; } = "";
    public string Learned { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string? ResourceUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class WorkShowcase
{
    public int? AuthorId { get; set; }
    public string Title { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Kind { get; set; } = "frame";
    public string? ResourceUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CompanionWorkRequest
{
    [Required, MaxLength(40)] public string Action { get; set; } = "";
    [Required] public uint? Version { get; set; }
    [Required] public DateTime? ExpectedUpdatedAt { get; set; }
    public Guid? EntryId { get; set; }
    public int? RecipientId { get; set; }
    public bool AssignOnAccept { get; set; }
    [MaxLength(20)] public string? Kind { get; set; }
    [MaxLength(120)] public string? Title { get; set; }
    [MaxLength(400)] public string? Summary { get; set; }
    [MaxLength(400)] public string? NextStep { get; set; }
    [MaxLength(400)] public string? Message { get; set; }
    [MaxLength(400)] public string? Criteria { get; set; }
    [MaxLength(400)] public string? Tried { get; set; }
    [MaxLength(400)] public string? Learned { get; set; }
    [MaxLength(1000)] public string? ResourceUrl { get; set; }
}

public sealed record CompanionWorkResponse(int TaskId, string TaskTitle, int? TeamId,
    TaskItemStatus TaskStatus, string? Tags, uint Version, DateTime UpdatedAt, int ViewerId,
    bool IsTeamOwner, Dictionary<int, string> People, WorkSavepoint? Savepoint,
    WorkHelp? Help, WorkHandoff? Handoff, List<WorkNote> Notes, WorkShowcase? Showcase)
{
    public List<WorkHelp> Thanks { get; init; } = [];
}

public sealed record WorkNoteMatch(int TaskId, string TaskTitle, WorkNote Note, string AuthorName, string MatchReason);
public sealed record WorkThanksMatch(int TaskId, string TaskTitle, string AuthorName, string HelperName, DateTime? ClosedAt);
public sealed record CompanionDashboard(string WeekStart, int NotesThisWeek,
    List<CompanionWorkResponse> Savepoints, List<CompanionWorkResponse> Help,
    List<CompanionWorkResponse> Handoffs, List<CompanionWorkResponse> Showcase,
    List<WorkNoteMatch> RecentNotes)
{
    public List<WorkThanksMatch> RecentThanks { get; init; } = [];
}
