using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public sealed record ChecklistItem
{
    [Required, MaxLength(120)]
    public string Text { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
}

public sealed record UndoReceipt(Guid Token, DateTime ExpiresAt);
public sealed record WeeklyDay(string Date, int Completed);
public sealed record WeeklyReviewResponse(string WeekStart, string WeekEnd, int ThisWeek,
    int LastWeek, int Difference, List<WeeklyDay> Days, string Message);

public sealed class ManageTaskTagRequest
{
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required, RegularExpression("^(rename|delete|merge)$")] public string Action { get; set; } = string.Empty;
    [MaxLength(50)] public string? TargetName { get; set; }
}
