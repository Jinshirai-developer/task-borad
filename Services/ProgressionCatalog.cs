using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

// Display preferences and species are choices, not rewards or productivity gates.
public static class ProgressionCatalog
{
    private sealed record Option(string Id, string Name, int RequiredLevel);

    private static readonly Option[] Pets =
    [
        new("dog", "いぬ", 1), new("cat", "ねこ", 1), new("rabbit", "うさぎ", 1),
        new("fox", "きつね", 1), new("panda", "パンダ", 1), new("dragon", "ドラゴン", 1)
    ];
    private static readonly Option[] Themes =
    [
        new("classic", "クラシック", 1), new("retro", "Windows風", 1), new("light", "ライト", 1), new("dark", "ダーク", 1),
        new("forest", "森", 1), new("sunset", "夕焼け", 1)
    ];
    private static readonly Option[] Layouts =
    [
        new("board", "ボード", 1), new("list", "リスト", 1), new("compact", "コンパクト", 1),
        new("gallery", "カード一覧", 1), new("focus", "集中", 1)
    ];

    public static UnlockCatalogResponse CreateResponse(int level, int totalExperience) => new(
        Math.Max(1, level), Math.Max(0, totalExperience),
        Describe(Pets, level, totalExperience), Describe(Themes, level, totalExperience), Describe(Layouts, level, totalExperience));

    private static List<UnlockOptionResponse> Describe(IEnumerable<Option> options, int level, int experience) =>
        options.Select(option => new UnlockOptionResponse(option.Id, option.Name, option.RequiredLevel,
            level >= option.RequiredLevel, level >= option.RequiredLevel ? 0 : Math.Max(0, TotalExperienceForLevel(option.RequiredLevel) - experience))).ToList();

    public static int TotalExperienceForLevel(int level) => checked(25 * (level - 1) * (level + 2));

    public static void RequirePet(string species, int level) => Require(Pets, species, level);

    public static void RequirePreferences(string theme, string layout, int level)
    {
        Require(Themes, theme, level);
        Require(Layouts, layout, level);
    }

    private static void Require(IEnumerable<Option> options, string id, int level)
    {
        var option = options.SingleOrDefault(item => item.Id == id)
            ?? throw new TeamOperationException("invalid_option", "対応していない選択肢です。", 400);
        if (level < option.RequiredLevel)
            throw new TeamOperationException("unlock_required", $"「{option.Name}」はLv.{option.RequiredLevel}で解放されます。現在はLv.{Math.Max(1, level)}です。", 403);
    }

    // Only repair unknown values. A correct task reversal must not reset preferences.
    public static void EnforceSelections(AppDbContext context, PetProfile pet)
    {
        if (!Pets.Any(option => option.Id == pet.Species && option.RequiredLevel <= pet.Level))
            pet.Species = "dog";
        var user = context.UserProfiles.SingleOrDefault(item => item.Id == pet.UserProfileId);
        if (user == null) return;
        var theme = Themes.Any(option => option.Id == user.Theme && option.RequiredLevel <= pet.Level) ? user.Theme : "classic";
        var layout = Layouts.Any(option => option.Id == user.Layout && option.RequiredLevel <= pet.Level) ? user.Layout : "board";
        if (theme == user.Theme && layout == user.Layout) return;
        user.Theme = theme;
        user.Layout = layout;
        user.UpdatedAt = DateTime.UtcNow;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
    }
}
