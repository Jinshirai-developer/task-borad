namespace TaskApi.Models;

public sealed class EmailOutboxMessage
{
    public long Id { get; set; }
    public int UserProfileId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string ProtectedPayload { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
