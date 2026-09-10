namespace TaskApi.Models;

public sealed class TaskTagDefinition
{
    public int Id { get; set; }
    public int? UserProfileId { get; set; }
    public int? TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
