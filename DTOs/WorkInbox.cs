namespace TaskApi.DTOs;

// Only actionable, shared request metadata. Never include private savepoints or whole task snapshots.
public sealed record WorkInboxItem(Guid Id, int TeamId, string TeamName, int TaskId, string TaskTitle,
    string Kind, string Audience, string Message, DateTime CreatedAt)
{
    public string AuthorName { get; init; } = "";
    public string RecipientName { get; init; } = "";
    public string? HelperName { get; init; }
    public string RequestKind { get; init; } = "";
    public string? Criteria { get; init; }
    public string Status { get; init; } = "";
    public bool AssignOnAccept { get; init; }
    public uint Version { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<string> Actions { get; init; } = [];
}
public sealed record WorkInboxResponse(List<WorkInboxItem> Items, int DirectCount, int TeamCount)
{
    public int HelpingCount { get; init; }
    public int SentCount { get; init; }
}
