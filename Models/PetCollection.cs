namespace TaskApi.Models;

public sealed class PetCollection
{
    public int PetProfileId { get; set; }
    public string Stage { get; set; } = "base";
    public int? HatLevel { get; set; }
    public int? BowLevel { get; set; }
    public int? MatLevel { get; set; }
}

public sealed class PetRewardChoice
{
    public int PetProfileId { get; set; }
    public int Level { get; set; }
    public string Choice { get; set; } = "hat";
    public DateTime ClaimedAt { get; set; }
}

public sealed class PetMemory
{
    public int PetProfileId { get; set; }
    public string Key { get; set; } = "";
    public DateTime UnlockedAt { get; set; }
}
