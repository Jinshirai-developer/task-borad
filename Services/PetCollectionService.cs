using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

// Choices are one per authenticated pet and level, never consumable currency.
// The same admission/PG advisory lock as task completion makes claims and undo atomic.
public sealed class PetCollectionService(AppDbContext context, PetService pets)
{
    private static readonly string[] Tones = ["青空", "すみれ", "さくら", "いちご", "夕焼け", "みかん", "ひまわり", "若葉", "森", "ミント", "海", "あじさい", "ラベンダー", "桃", "珊瑚", "秋色", "月", "エメラルド", "湖", "星空"];
    private static readonly (string Id, string Name, int Level, int Row)[] Growth =
        [("base", "いつもの相棒", 1, 0), ("explorer", "小さな冒険家", 5, 1), ("grown", "頼れる相棒", 10, 2), ("festival", "星のお祝い姿", 20, 3)];
    private static readonly (string Key, string Name, string Description, int Row, int Column)[] MemoryCatalog =
    [
        ("first", "はじめの一歩", "タスクを初めて完了", 0, 1),
        ("tasks10", "10個の達成", "タスクを10個完了", 0, 3),
        ("tasks50", "50個の達成", "タスクを50個完了", 1, 3),
        ("tasks100", "100個の達成", "タスクを100個完了", 2, 3),
        ("level5", "小さな冒険", "Lv.5に到達", 1, 0),
        ("level10", "頼れる相棒", "Lv.10に到達", 2, 0),
        ("level20", "星のお祝い", "Lv.20に到達", 3, 3),
        ("pet", "なかよしの時間", "初めてなでる", 0, 1),
        ("treat", "おやつの時間", "初めておやつをあげる", 0, 2),
        ("rest", "おやすみ", "初めて休憩する", 0, 4)
    ];

    public PetCollectionResponse Get(int userId) => Run(userId, _ => { });

    public PetCollectionResponse Claim(int userId, ClaimPetRewardRequest request) => Run(userId, pet =>
    {
        if (request.Level is < 1 or > 20 || request.Choice is not ("hat" or "bow" or "mat"))
            throw new TeamOperationException("invalid_reward", "対応していないごほうびです。", 400);
        var existing = context.PetRewardChoices.SingleOrDefault(item => item.PetProfileId == pet.Id && item.Level == request.Level);
        if (request.Level > PetRewardArtCatalog.MaxRewardLevel && existing == null)
            throw new TeamOperationException("reward_retired", "新しいごほうびはLv.1〜5です。過去に受け取った品は引き続き使えます。", 400);
        if (existing != null)
        {
            if (existing.Choice != request.Choice)
                throw new TeamOperationException("reward_already_claimed", "このレベルのごほうびは受け取り済みです。選び直すことはできません。", 409);
            return; // A retry of the same claim is idempotent.
        }
        if (request.Level > pet.Level) throw Locked();
        context.PetRewardChoices.Add(new() { PetProfileId = pet.Id, Level = request.Level, Choice = request.Choice, ClaimedAt = DateTime.UtcNow });
    });

    public PetCollectionResponse Equip(int userId, PetAppearanceRequest request) => Run(userId, pet =>
    {
        var stage = Growth.FirstOrDefault(item => item.Id == request.Stage);
        if (stage.Id == null) throw new TeamOperationException("invalid_stage", "対応していない姿です。", 400);
        if (!StageUnlocked(context, pet, stage.Level)) throw Locked();
        // Validate ALL fields before changing any of them.
        foreach (var (level, kind) in new[] { (request.HatLevel, "hat"), (request.BowLevel, "bow"), (request.MatLevel, "mat") })
        {
            if (level == null) continue;
            if (level < 1 || level > 20) throw new TeamOperationException("invalid_reward", "対応していないごほうびです。", 400);
            if (!context.PetRewardChoices.Any(item => item.PetProfileId == pet.Id && item.Level == level && item.Choice == kind)) throw Locked();
        }
        var appearance = context.PetCollections.Single(item => item.PetProfileId == pet.Id);
        appearance.Stage = request.Stage;
        appearance.HatLevel = request.HatLevel;
        appearance.BowLevel = request.BowLevel;
        appearance.MatLevel = request.MatLevel;
    });

    public PetCollectionResponse Interact(int userId, PetInteractionRequest request) => Run(userId, pet =>
    {
        if (request.Action is not ("pet" or "treat" or "rest"))
            throw new TeamOperationException("invalid_interaction", "対応していない触れ合いです。", 400);
        RecordMemory(context, pet, request.Action, DateTime.UtcNow);
        // No XP, energy, streak, task receipts, or reward eligibility changes.
    });

    private PetCollectionResponse Run(int userId, Action<PetProfile> action)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var profile = pets.GetProfile(userId); // Authenticated lookup; never accepts a client pet/user ID.
        var pet = context.PetProfiles.Single(item => item.Id == profile.Id);
        if (!context.PetCollections.Any(item => item.PetProfileId == pet.Id))
        {
            context.PetCollections.Add(new() { PetProfileId = pet.Id });
            context.SaveChanges();
        }
        RecordMilestones(context, pet, DateTime.UtcNow);
        EnforceEquipment(context, pet);
        action(pet);
        context.SaveChanges();
        var response = Describe(pet);
        scope.Commit();
        return response;
    }

    public static void RecordMilestones(AppDbContext db, PetProfile pet, DateTime now)
    {
        // New pets may not have a database ID yet; GET backfills once saved.
        if (pet.Id <= 0) return;
        foreach (var (threshold, key) in new[] { (1, "first"), (10, "tasks10"), (50, "tasks50"), (100, "tasks100") })
            if (pet.CompletedTaskCount >= threshold) RecordMemory(db, pet, key, now);
        foreach (var level in new[] { 5, 10, 20 })
            if (pet.Level >= level) RecordMemory(db, pet, $"level{level}", now);
    }

    public static void RecordSavedMilestones(AppDbContext db, int userId, DateTime now)
    {
        // TaskService calls this after assigning a new pet's database ID, but before
        // committing its transaction. Even an immediate undo without GET keeps "first".
        var pet = db.PetProfiles.Local.SingleOrDefault(item => item.UserProfileId == userId);
        if (pet == null) return;
        RecordMilestones(db, pet, now);
        if (db.ChangeTracker.Entries<PetMemory>().Any(item => item.State == EntityState.Added)) db.SaveChanges();
    }

    private static void RecordMemory(AppDbContext db, PetProfile pet, string key, DateTime now)
    {
        if (!db.PetMemories.Local.Any(item => item.PetProfileId == pet.Id && item.Key == key)
            && !db.PetMemories.Any(item => item.PetProfileId == pet.Id && item.Key == key))
            db.PetMemories.Add(new() { PetProfileId = pet.Id, Key = key, UnlockedAt = now });
    }

    public static void EnforceEquipment(AppDbContext db, PetProfile pet)
    {
        var appearance = db.PetCollections.Local.FirstOrDefault(item => item.PetProfileId == pet.Id)
            ?? db.PetCollections.SingleOrDefault(item => item.PetProfileId == pet.Id);
        if (appearance == null) return;
        if (!Growth.Any(item => item.Id == appearance.Stage)) appearance.Stage = "base";
        // A stored valid stage proves that it was equipped under the old rules.
        // Backfill that milestone without inventing an earlier achievement date.
        var stage = Growth.Single(item => item.Id == appearance.Stage);
        if (stage.Level > 1) RecordMemory(db, pet, $"level{stage.Level}", DateTime.UtcNow);
        var owned = db.PetRewardChoices.Where(item => item.PetProfileId == pet.Id).ToList();
        if (!owned.Any(item => item.Level == appearance.HatLevel && item.Choice == "hat")) appearance.HatLevel = null;
        if (!owned.Any(item => item.Level == appearance.BowLevel && item.Choice == "bow")) appearance.BowLevel = null;
        if (!owned.Any(item => item.Level == appearance.MatLevel && item.Choice == "mat")) appearance.MatLevel = null;
        // Earned cosmetics remain usable after XP reversal, but unowned ones do not.
    }

    private static bool StageUnlocked(AppDbContext db, PetProfile pet, int level) => level <= pet.Level
        || db.PetMemories.Local.Any(item => item.PetProfileId == pet.Id && item.Key == $"level{level}")
        || db.PetMemories.Any(item => item.PetProfileId == pet.Id && item.Key == $"level{level}");

    private PetCollectionResponse Describe(PetProfile pet)
    {
        var appearance = context.PetCollections.Single(item => item.PetProfileId == pet.Id);
        var choices = context.PetRewardChoices.Where(item => item.PetProfileId == pet.Id).ToDictionary(item => item.Level);
        var memories = context.PetMemories.Where(item => item.PetProfileId == pet.Id).ToDictionary(item => item.Key);
        return new(pet.Level, pet.TotalExperience, pet.Species,
            new() { Stage = appearance.Stage, HatLevel = appearance.HatLevel, BowLevel = appearance.BowLevel, MatLevel = appearance.MatLevel },
            Enumerable.Range(1, PetRewardArtCatalog.MaxRewardLevel)
                .Concat(choices.Keys.Where(level => level > PetRewardArtCatalog.MaxRewardLevel)).OrderBy(level => level)
                .Select(level => new PetGiftLevel(level, level <= pet.Level || choices.ContainsKey(level),
                choices.GetValueOrDefault(level)?.Choice, choices.GetValueOrDefault(level)?.ClaimedAt,
                new[] { "hat", "bow", "mat" }.Select(kind => level <= PetRewardArtCatalog.MaxRewardLevel
                    ? new PetGiftOption(kind, PetRewardArtCatalog.Get(pet.Species, level, kind).Name,
                        0, 0, PetRewardArtCatalog.Get(pet.Species, level, kind).Image)
                    : new PetGiftOption(kind,
                    $"{Tones[level - 1]}の{(kind == "hat" ? "帽子" : kind == "bow" ? "リボン" : "クッション")}",
                    (level - 1) / 7, (level - 1) * 25)).ToList(), level > PetRewardArtCatalog.MaxRewardLevel)).ToList(),
            Growth.Select(item => new PetGrowthStage(item.Id, item.Name, item.Level, item.Row, item.Level <= pet.Level || memories.ContainsKey($"level{item.Level}"))).ToList(),
            MemoryCatalog.Select(item => new PetMemoryResponse(item.Key, item.Name, item.Description, item.Row, item.Column,
                memories.GetValueOrDefault(item.Key)?.UnlockedAt)).ToList());
    }

    private static TeamOperationException Locked() => new("unlock_required", "必要レベルと、受け取り済みのごほうびを確認してください。", 403);
}
