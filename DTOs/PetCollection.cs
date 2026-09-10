using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public sealed class ClaimPetRewardRequest
{
    [Range(1, 20)] public int Level { get; set; }
    [Required, RegularExpression("^(hat|bow|mat)$")] public string Choice { get; set; } = "";
}

public sealed class PetAppearanceRequest
{
    [Required, RegularExpression("^(base|explorer|grown|festival)$")] public string Stage { get; set; } = "base";
    [Range(1, 20)] public int? HatLevel { get; set; }
    [Range(1, 20)] public int? BowLevel { get; set; }
    [Range(1, 20)] public int? MatLevel { get; set; }
}

public sealed class PetInteractionRequest
{
    [Required, RegularExpression("^(pet|treat|rest)$")] public string Action { get; set; } = "";
}

public sealed record PetGiftOption(string Id, string Name, int Shape, int Hue, string? Image = null);
public sealed record PetGiftLevel(int Level, bool Available, string? ClaimedChoice, DateTime? ClaimedAt, List<PetGiftOption> Options, bool IsLegacy = false);
public sealed record PetGrowthStage(string Id, string Name, int RequiredLevel, int Row, bool Available);
public sealed record PetMemoryResponse(string Key, string Name, string Description, int Row, int Column, DateTime? UnlockedAt);
public sealed record PetCollectionResponse(int Level, int TotalExperience, string Species, PetAppearanceRequest Appearance,
    List<PetGiftLevel> Rewards, List<PetGrowthStage> Stages, List<PetMemoryResponse> Memories, int MaxRewardLevel = 5);
