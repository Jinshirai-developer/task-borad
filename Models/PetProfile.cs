namespace TaskApi.Models;

public class PetProfile
{
    public int Id { get; set; }

    public int UserProfileId { get; set; }

    public string Name { get; set; } = "Task Pet";

    public string Species { get; set; } = "dog";

    public int Level { get; set; } = 1;

    public int Experience { get; set; }

    public int TotalExperience { get; set; }

    public int CompletedTaskCount { get; set; }

    public int StreakDays { get; set; }

    public int Energy { get; set; } = 80;

    public string Mood { get; set; } = "Idle";

    public DateTime? LastCompletedAt { get; set; }

    // Only pre-ledger activity with an unknown source is retained as a baseline.
    public DateTime? LegacyLastCompletedAt { get; set; }

    public int LegacyStreakDays { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public uint Version { get; set; }
}
