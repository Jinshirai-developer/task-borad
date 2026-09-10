namespace TaskApi.DTOs;

public sealed record UnlockOptionResponse(string Id, string Name, int RequiredLevel, bool Unlocked, int ExperienceRemaining);

public sealed record UnlockCatalogResponse(int Level, int TotalExperience,
    IReadOnlyList<UnlockOptionResponse> Pets,
    IReadOnlyList<UnlockOptionResponse> Themes,
    IReadOnlyList<UnlockOptionResponse> Layouts);
