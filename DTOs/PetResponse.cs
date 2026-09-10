namespace TaskApi.DTOs;

public class PetResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Species { get; set; } = "dog";

    public string Title { get; set; } = string.Empty;

    public int Level { get; set; }

    public int Experience { get; set; }

    public int ExperienceToNextLevel { get; set; }

    public int ExperienceProgress { get; set; }

    public int ExperienceRemaining { get; set; }

    public int TotalExperience { get; set; }

    public int CompletedTaskCount { get; set; }

    public int StreakDays { get; set; }

    public int Energy { get; set; }

    public string Mood { get; set; } = string.Empty;

    public string MoodLabel { get; set; } = string.Empty;

    public string EnergyLabel { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public List<string> Achievements { get; set; } = [];

    public DateTime? LastCompletedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
