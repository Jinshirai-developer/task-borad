using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class ProgressionTests
{
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static UserProfileService Profiles(AppDbContext db) => new(db, NullLogger<UserProfileService>.Instance);
    private static PetService Pets(AppDbContext db) => new(db, NullLogger<PetService>.Instance, Profiles(db));
    private static TaskService Tasks(AppDbContext db) => new(db, NullLogger<TaskService>.Instance, Pets(db), Profiles(db));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 100)]
    [InlineData(3, 250)]
    [InlineData(4, 450)]
    [InlineData(5, 700)]
    [InlineData(6, 1000)]
    [InlineData(7, 1350)]
    public void UnlockThresholds_MatchPetLevelCosts(int level, int experience) =>
        Assert.Equal(experience, ProgressionCatalog.TotalExperienceForLevel(level));

    [Fact]
    public void Catalog_ContainsEveryChoiceAndPreservesExistingLevelOneChoices()
    {
        var catalog = ProgressionCatalog.CreateResponse(1, 0);
        Assert.Equal(6, catalog.Pets.Count);
        Assert.Equal(6, catalog.Themes.Count);
        Assert.Equal(5, catalog.Layouts.Count);
        Assert.Equal(new[] { "dog", "cat", "rabbit", "fox", "panda", "dragon" }, catalog.Pets.Where(item => item.Unlocked).Select(item => item.Id));
        Assert.Equal(new[] { "classic", "retro", "light", "dark", "forest", "sunset" }, catalog.Themes.Where(item => item.Unlocked).Select(item => item.Id));
        Assert.Equal(new[] { "board", "list", "compact", "gallery", "focus" }, catalog.Layouts.Where(item => item.Unlocked).Select(item => item.Id));
        Assert.Equal(0, catalog.Pets.Single(item => item.Id == "fox").ExperienceRemaining);
    }

    [Fact]
    public void Catalog_UnlocksAtExactLevelsAndReportsRemainingExperience()
    {
        var target = ProgressionCatalog.CreateResponse(7, 1350);
        foreach (var option in target.Pets.Concat(target.Themes).Concat(target.Layouts))
        {
            Assert.True(option.Unlocked);
            Assert.Equal(0, option.ExperienceRemaining);
            if (option.RequiredLevel == 1) continue;
            var locked = ProgressionCatalog.CreateResponse(option.RequiredLevel - 1,
                ProgressionCatalog.TotalExperienceForLevel(option.RequiredLevel) - 25);
            var actual = locked.Pets.Concat(locked.Themes).Concat(locked.Layouts).Single(item => item.Id == option.Id);
            Assert.False(actual.Unlocked);
            Assert.Equal(25, actual.ExperienceRemaining);
        }
    }

    [Fact]
    public void CompletionAndUndo_KeepSpeciesAndName()
    {
        using var db = Database();
        var profiles = Profiles(db);
        var user = profiles.GetOrCreateByKey("progress-owner");
        var pets = Pets(db);
        pets.UpdateProfile(user.Id, new() { Name = "Mochi" });
        var tasks = Tasks(db);
        var completed = Enumerable.Range(0, 4).Select(index => tasks.Create(user.Id,
            new() { Title = $"Done {index}", Status = TaskItemStatus.Done })).ToList();

        Assert.True(profiles.GetUnlocks(user.Id).Pets.Single(option => option.Id == "fox").Unlocked);
        Assert.Equal("fox", pets.UpdateProfile(user.Id, new() { Name = "Mochi", Species = "fox" }).Species);
        tasks.Update(user.Id, completed[0].Id, new() { Title = "Reopened", Status = TaskItemStatus.Doing });

        var pet = pets.GetProfile(user.Id);
        Assert.Equal(75, pet.TotalExperience);
        Assert.Equal(1, pet.Level);
        Assert.Equal("Mochi", pet.Name);
        Assert.Equal("fox", pet.Species);
        Assert.True(profiles.GetUnlocks(user.Id).Pets.Single(option => option.Id == "fox").Unlocked);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => pets.UpdateProfile(user.Id,
            new() { Name = "Should not change", Species = "invalid" })).StatusCode);
        Assert.Equal("Mochi", pets.GetProfile(user.Id).Name);

        tasks.Update(user.Id, completed[0].Id, new() { Title = "Done again", Status = TaskItemStatus.Done });
        Assert.Equal(100, pets.GetProfile(user.Id).TotalExperience);
        Assert.Equal("fox", pets.GetProfile(user.Id).Species);
        Assert.True(profiles.GetUnlocks(user.Id).Pets.Single(option => option.Id == "fox").Unlocked);
    }

    [Fact]
    public void LevelDrop_KeepsPreferencesAndOtherUsersUnchanged()
    {
        using var db = Database();
        var profiles = Profiles(db);
        var user = profiles.GetOrCreateByKey("owner");
        var other = profiles.GetOrCreateByKey("other");
        profiles.UpdatePreferences(other.Id, new() { Theme = "dark", Layout = "list" });
        var tasks = Tasks(db);
        var completed = Enumerable.Range(0, 10).Select(index => tasks.Create(user.Id,
            new() { Title = $"Completed {index}", Status = TaskItemStatus.Done })).ToList();
        profiles.UpdatePreferences(user.Id, new() { Theme = "forest", Layout = "compact" });
        Pets(db).UpdateProfile(user.Id, new() { Name = "Fox", Species = "fox" });

        tasks.Update(user.Id, completed[0].Id, new() { Title = "Reopened", Status = TaskItemStatus.Todo });
        db.ChangeTracker.Clear();
        Assert.Equal(2, Pets(db).GetProfile(user.Id).Level);
        Assert.Equal("fox", Pets(db).GetProfile(user.Id).Species);
        Assert.Equal("forest", profiles.GetPreferences(user.Id).Theme);
        Assert.Equal("compact", profiles.GetPreferences(user.Id).Layout);
        Assert.Equal("dark", profiles.GetPreferences(other.Id).Theme);
        Assert.Equal("list", profiles.GetPreferences(other.Id).Layout);
    }

    [Fact]
    public void RetroTheme_RemainsAvailableAfterLosingAReward()
    {
        using var db = Database();
        var profiles = Profiles(db);
        var owner = profiles.GetOrCreateByKey("retro-owner");
        var tasks = Tasks(db);
        var done = tasks.Create(owner.Id, new() { Title = "Completed", Status = TaskItemStatus.Done });
        profiles.UpdatePreferences(owner.Id, new() { Theme = "retro", Layout = "list" });
        tasks.Update(owner.Id, done.Id, new() { Title = "Reopened", Status = TaskItemStatus.Doing });
        Assert.Equal(0, Pets(db).GetProfile(owner.Id).TotalExperience);
        Assert.Equal("retro", profiles.GetPreferences(owner.Id).Theme);
        Assert.Equal("list", profiles.GetPreferences(owner.Id).Layout);
    }

    [Fact]
    public async Task UnlockEndpoint_RequiresConfirmedAuthenticationAndCannotBeForgedFromRequest()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/user/unlocks")).StatusCode);
        using var unconfirmed = factory.CreateSecureClient();
        await unconfirmed.RegisterAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await unconfirmed.GetAsync("/api/user/unlocks")).StatusCode);
        await factory.RegisterConfirmedAsync(client);
        var catalog = await client.GetFromJsonAsync<UnlockCatalogResponse>("/api/user/unlocks");
        Assert.NotNull(catalog);
        Assert.Equal(1, catalog.Level);
        Assert.All(catalog.Pets.Concat(catalog.Themes).Concat(catalog.Layouts), item => Assert.True(item.Unlocked));
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/user/preferences",
            new { theme = "forest", layout = "focus", level = 99, totalExperience = 999999 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet",
            new { name = "Dragon", species = "dragon", level = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/user/preferences",
            new { theme = "invalid", layout = "focus" })).StatusCode);
        var actual = await client.GetFromJsonAsync<PetResponse>("/api/pet");
        Assert.Equal(1, actual!.Level);
        Assert.Equal(0, actual.TotalExperience);
        var unchanged = await client.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences");
        Assert.Equal("forest", unchanged?.Theme);
        Assert.Equal("focus", unchanged?.Layout);
    }

    private sealed record ErrorPayload(string Code);
}
