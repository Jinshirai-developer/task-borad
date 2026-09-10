using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class PetCollectionTests
{
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static UserProfileService Profiles(AppDbContext db) => new(db, NullLogger<UserProfileService>.Instance);
    private static PetService Pets(AppDbContext db) => new(db, NullLogger<PetService>.Instance, Profiles(db));
    private static PetCollectionService Collection(AppDbContext db) => new(db, Pets(db));
    private static TaskService Tasks(AppDbContext db) => new(db, NullLogger<TaskService>.Instance, Pets(db), Profiles(db));

    [Fact]
    public void Catalog_OffersFiveLevelsWithThreeSpeciesSpecificChoicesAndFourOptionalStages()
    {
        using var db = Database();
        var user = Profiles(db).GetOrCreateByKey("owner");
        var result = Collection(db).Get(user.Id);
        Assert.Equal(5, result.Rewards.Count);
        Assert.Equal(5, result.MaxRewardLevel);
        Assert.All(result.Rewards, reward => Assert.Equal(3, reward.Options.Count));
        Assert.All(result.Rewards, reward => Assert.False(reward.IsLegacy));
        Assert.All(result.Rewards.SelectMany(reward => reward.Options), option => Assert.StartsWith("assets/pet/rewards-v2/dog/", option.Image));
        Assert.Single(result.Rewards, reward => reward.Available);
        Assert.Equal(new[] { 1, 5, 10, 20 }, result.Stages.Select(stage => stage.RequiredLevel));
        Assert.Equal("base", result.Appearance.Stage);
        Assert.Equal(10, result.Memories.Count);
        Assert.All(result.Memories, memory => Assert.Null(memory.UnlockedAt));
    }

    [Fact]
    public void Claims_ArePersistentIdempotentAndNeverChangeable()
    {
        using var db = Database();
        var user = Profiles(db).GetOrCreateByKey("owner");
        var service = Collection(db);
        var first = service.Claim(user.Id, new() { Level = 1, Choice = "hat" });
        var second = service.Claim(user.Id, new() { Level = 1, Choice = "hat" });
        Assert.Equal(first.Rewards[0].ClaimedAt, second.Rewards[0].ClaimedAt);
        Assert.Equal(409, Assert.Throws<TeamOperationException>(() => service.Claim(user.Id, new() { Level = 1, Choice = "bow" })).StatusCode);
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => service.Claim(user.Id, new() { Level = 2, Choice = "hat" })).StatusCode);
        Assert.Single(db.PetRewardChoices);
        db.ChangeTracker.Clear();
        Assert.Equal("hat", Collection(db).Get(user.Id).Rewards[0].ClaimedChoice);
    }

    [Theory]
    [InlineData(0, "hat")]
    [InlineData(21, "hat")]
    [InlineData(6, "hat")]
    [InlineData(20, "bow")]
    [InlineData(1, "xp")]
    public void InvalidClaims_AreRejected(int level, string choice)
    {
        using var db = Database();
        var user = Profiles(db).GetOrCreateByKey("owner");
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => Collection(db).Claim(user.Id, new() { Level = level, Choice = choice })).StatusCode);
        Assert.Empty(db.PetRewardChoices);
    }

    [Fact]
    public void Equipment_IsOwnedAndAtomicAndCanBeRemoved()
    {
        using var db = Database();
        var owner = Profiles(db).GetOrCreateByKey("owner");
        var other = Profiles(db).GetOrCreateByKey("other");
        var service = Collection(db);
        service.Claim(owner.Id, new() { Level = 1, Choice = "hat" });
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => service.Equip(other.Id, new() { HatLevel = 1 })).StatusCode);
        service.Equip(owner.Id, new() { HatLevel = 1 });
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => service.Equip(owner.Id, new() { BowLevel = 1 })).StatusCode);
        Assert.Equal(1, service.Get(owner.Id).Appearance.HatLevel);
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => service.Equip(owner.Id, new() { Stage = "explorer" })).StatusCode);
        Assert.Equal(1, service.Get(owner.Id).Appearance.HatLevel);
        Assert.Null(service.Equip(owner.Id, new()).Appearance.HatLevel);
    }

    [Fact]
    public void SpeciesCatalog_HasNinetyUniqueArtPathsAndSpeciesChangesNeverGrantExtraChoices()
    {
        var paths = new HashSet<string>();
        foreach (var species in new[] { "dog", "cat", "rabbit", "fox", "panda", "dragon" })
            foreach (var level in Enumerable.Range(1, 5))
                foreach (var kind in new[] { "hat", "bow", "mat" })
                {
                    var art = PetRewardArtCatalog.Get(species, level, kind);
                    Assert.False(string.IsNullOrWhiteSpace(art.Name));
                    Assert.True(paths.Add(art.Image));
                    Assert.Contains($"/{species}/lv-{level}-{kind}.png", art.Image);
                }
        Assert.Equal(90, paths.Count);
        using var db = Database();
        var owner = Profiles(db).GetOrCreateByKey("species");
        var service = Collection(db);
        var before = service.Claim(owner.Id, new() { Level = 1, Choice = "hat" });
        var pet = db.PetProfiles.Single();
        pet.Species = "cat";
        db.SaveChanges();
        var after = service.Get(owner.Id);
        Assert.Equal("cat", after.Species);
        Assert.Equal(before.Rewards[0].ClaimedAt, after.Rewards[0].ClaimedAt);
        Assert.Equal("hat", after.Rewards[0].ClaimedChoice);
        Assert.All(after.Rewards.SelectMany(reward => reward.Options), option => Assert.Contains("/cat/", option.Image));
        Assert.Equal(409, Assert.Throws<TeamOperationException>(() => service.Claim(owner.Id, new() { Level = 1, Choice = "bow" })).StatusCode);
        Assert.Single(db.PetRewardChoices);
    }

    [Fact]
    public void LegacyOwnedRewards_RemainUsableButNoNewRewardsAboveFiveAreIssued()
    {
        using var db = Database();
        var owner = Profiles(db).GetOrCreateByKey("legacy");
        var tasks = Tasks(db);
        var completed = Enumerable.Range(0, 40).Select(i => tasks.Create(owner.Id, new() { Title = $"Legacy {i}", Status = TaskItemStatus.Done })).ToList();
        var service = Collection(db);
        var pet = db.PetProfiles.Single();
        var claimedAt = DateTime.UtcNow.AddDays(-1);
        db.PetRewardChoices.Add(new() { PetProfileId = pet.Id, Level = 6, Choice = "hat", ClaimedAt = claimedAt });
        db.SaveChanges();
        var result = service.Get(owner.Id);
        Assert.Equal(6, result.Level);
        Assert.Equal(6, result.Rewards.Count);
        var legacy = Assert.Single(result.Rewards, reward => reward.IsLegacy);
        Assert.Equal(6, legacy.Level);
        Assert.Equal(claimedAt, legacy.ClaimedAt);
        Assert.All(legacy.Options, option => Assert.Null(option.Image));
        Assert.Equal(6, service.Equip(owner.Id, new() { HatLevel = 6 }).Appearance.HatLevel);
        service.Claim(owner.Id, new() { Level = 6, Choice = "hat" });
        Assert.Equal(409, Assert.Throws<TeamOperationException>(() => service.Claim(owner.Id, new() { Level = 6, Choice = "bow" })).StatusCode);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => service.Claim(owner.Id, new() { Level = 7, Choice = "mat" })).StatusCode);
        Assert.Single(db.PetRewardChoices);
        tasks.Update(owner.Id, completed[0].Id, new() { Title = "Undo", Status = TaskItemStatus.Todo });
        var dropped = service.Get(owner.Id);
        Assert.Equal(dropped.Rewards.Single(reward => reward.ClaimedChoice == "hat").Level, dropped.Appearance.HatLevel);
        Assert.Equal(claimedAt, dropped.Rewards.Single(reward => reward.IsLegacy).ClaimedAt);
    }

    [Fact]
    public void Undo_KeepsEarnedAppearanceChoicesAndMemoriesWithoutFarming()
    {
        using var db = Database();
        var owner = Profiles(db).GetOrCreateByKey("owner");
        var tasks = Tasks(db);
        var completed = Enumerable.Range(0, 28).Select(i => tasks.Create(owner.Id, new() { Title = $"Task {i}", Status = TaskItemStatus.Done })).ToList();
        var service = Collection(db);
        Assert.Equal(5, service.Get(owner.Id).Level);
        service.Claim(owner.Id, new() { Level = 1, Choice = "bow" });
        service.Claim(owner.Id, new() { Level = 5, Choice = "hat" });
        service.Equip(owner.Id, new() { Stage = "explorer", HatLevel = 5, BowLevel = 1 });
        var memoryTime = service.Get(owner.Id).Memories.Single(m => m.Key == "level5").UnlockedAt;
        tasks.Update(owner.Id, completed[0].Id, new() { Title = "Undo", Status = TaskItemStatus.Todo });
        db.ChangeTracker.Clear();
        var dropped = service.Get(owner.Id);
        Assert.Equal(4, dropped.Level);
        Assert.Equal("explorer", dropped.Appearance.Stage);
        Assert.Equal(dropped.Rewards.Single(reward => reward.ClaimedChoice == "hat").Level, dropped.Appearance.HatLevel);
        Assert.Equal(1, dropped.Appearance.BowLevel);
        Assert.Equal("hat", dropped.Rewards[4].ClaimedChoice);
        Assert.True(dropped.Rewards[4].Available);
        service.Equip(owner.Id, new() { Stage = "base" });
        Assert.Equal("explorer", service.Equip(owner.Id, new() { Stage = "explorer", HatLevel = 5, BowLevel = 1 }).Appearance.Stage);
        Assert.Equal("hat", service.Claim(owner.Id, new() { Level = 5, Choice = "hat" }).Rewards[4].ClaimedChoice);
        Assert.Equal(memoryTime, dropped.Memories.Single(m => m.Key == "level5").UnlockedAt);
        tasks.Update(owner.Id, completed[0].Id, new() { Title = "Redo", Status = TaskItemStatus.Done });
        Assert.Equal(409, Assert.Throws<TeamOperationException>(() => service.Claim(owner.Id, new() { Level = 5, Choice = "mat" })).StatusCode);
        Assert.Equal("explorer", service.Get(owner.Id).Appearance.Stage);
        Assert.Equal(5, service.Get(owner.Id).Appearance.HatLevel);
        Assert.Equal(2, db.PetRewardChoices.Count());
    }

    [Fact]
    public void Interactions_RecordOnceAndDoNotGrantGameplayBenefits()
    {
        using var db = Database();
        var user = Profiles(db).GetOrCreateByKey("owner");
        var before = Pets(db).GetProfile(user.Id);
        var service = Collection(db);
        foreach (var action in new[] { "pet", "treat", "rest", "pet", "rest" }) service.Interact(user.Id, new() { Action = action });
        var after = Pets(db).GetProfile(user.Id);
        Assert.Equal(before.TotalExperience, after.TotalExperience);
        Assert.Equal(before.Energy, after.Energy);
        Assert.Equal(before.CompletedTaskCount, after.CompletedTaskCount);
        Assert.Equal(3, db.PetMemories.Count());
        Assert.Empty(db.PetRewardChoices);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => service.Interact(user.Id, new() { Action = "xp" })).StatusCode);
    }

    [Fact]
    public void FirstCompletionMemory_SurvivesImmediateUndoBeforeAnyCollectionRead()
    {
        using var db = Database();
        var user = Profiles(db).GetOrCreateByKey("new-pet");
        Assert.Empty(db.PetProfiles);
        var tasks = Tasks(db);
        var task = tasks.Create(user.Id, new() { Title = "First", Status = TaskItemStatus.Done });
        tasks.Update(user.Id, task.Id, new() { Title = "Undo immediately", Status = TaskItemStatus.Todo });
        db.ChangeTracker.Clear();
        Assert.NotNull(Collection(db).Get(user.Id).Memories.Single(item => item.Key == "first").UnlockedAt);
        Assert.Equal(0, Pets(db).GetProfile(user.Id).TotalExperience);
    }

    [Fact]
    public async Task ConcurrentDifferentClaims_SaveExactlyOneChoiceAcrossContexts()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var setup = new AppDbContext(options);
        var user = Profiles(setup).GetOrCreateByKey("concurrent");
        Collection(setup).Get(user.Id);
        var statuses = await Task.WhenAll(new[] { "hat", "bow", "mat" }.Select(choice => Task.Run(() =>
        {
            using var db = new AppDbContext(options);
            try { Collection(db).Claim(user.Id, new() { Level = 1, Choice = choice }); return 200; }
            catch (TeamOperationException error) { return error.StatusCode; }
        })));
        Assert.Single(statuses, status => status == 200);
        Assert.Equal(2, statuses.Count(status => status == 409));
        Assert.Single(setup.PetRewardChoices);
    }

    [Fact]
    public async Task Routes_RequireConfirmedAuthenticationAndCsrfAndValidatePayloads()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/pet/collection")).StatusCode);
        using var unconfirmed = factory.CreateSecureClient();
        await unconfirmed.RegisterAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await unconfirmed.GetAsync("/api/pet/collection")).StatusCode);
        await factory.RegisterConfirmedAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pet/rewards", new { level = 1, choice = "hat" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/pet/rewards", new { level = 21, choice = "hat" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/pet/rewards", new { level = 6, choice = "hat", totalExperience = 999999 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/pet/rewards", new { level = 5, choice = "hat", totalExperience = 999999 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/pet/rewards", new { level = 1, choice = "hat" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet/appearance", new { stage = "base", hatLevel = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/pet/interactions", new { action = "pet" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet/appearance", new { stage = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/pet/interactions", new { action = "" })).StatusCode);
    }
}
