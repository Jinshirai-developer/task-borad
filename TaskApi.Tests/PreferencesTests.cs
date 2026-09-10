using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class PreferencesTests
{
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static UserProfileService Profiles(AppDbContext database) =>
        new(database, NullLogger<UserProfileService>.Instance);

    private static PetService Pets(AppDbContext database) =>
        new(database, NullLogger<PetService>.Instance, Profiles(database));

    [Fact]
    public void NewUser_DefaultsToClassicBoardAndDog()
    {
        using var database = Database();
        var user = Profiles(database).GetOrCreateByKey("new-owner");

        var preferences = Profiles(database).GetPreferences(user.Id);
        var pet = Pets(database).GetProfile(user.Id);

        Assert.Equal("classic", preferences.Theme);
        Assert.Equal("board", preferences.Layout);
        Assert.Equal("dog", pet.Species);
        Assert.Equal("Task Pet", pet.Name);
        Assert.Equal(0, pet.TotalExperience);
    }

    [Theory]
    [InlineData("classic", "board")]
    [InlineData("classic", "list")]
    [InlineData("retro", "board")]
    [InlineData("retro", "list")]
    [InlineData("light", "board")]
    [InlineData("light", "list")]
    [InlineData("dark", "board")]
    [InlineData("dark", "list")]
    public void Preferences_PersistEverySupportedCombinationWithoutChangingOtherUser(string theme, string layout)
    {
        using var database = Database();
        var profiles = Profiles(database);
        var ownerId = profiles.GetOrCreateByKey("owner").Id;
        var otherId = profiles.GetOrCreateByKey("other").Id;

        var updated = profiles.UpdatePreferences(ownerId, new() { Theme = theme, Layout = layout });
        database.ChangeTracker.Clear();

        Assert.Equal(theme, updated.Theme);
        Assert.Equal(layout, updated.Layout);
        Assert.Equal(theme, profiles.GetPreferences(ownerId).Theme);
        Assert.Equal(layout, profiles.GetPreferences(ownerId).Layout);
        Assert.Equal("classic", profiles.GetPreferences(otherId).Theme);
        Assert.Equal("board", profiles.GetPreferences(otherId).Layout);
    }

    [Theory]
    [InlineData("unknown", "board")]
    [InlineData("Dark", "board")]
    [InlineData("classic", "grid")]
    [InlineData("classic", "List")]
    [InlineData("", "board")]
    [InlineData("light", "")]
    public void InvalidPreferences_AreRejectedWithoutPersistence(string theme, string layout)
    {
        using var database = Database();
        var profiles = Profiles(database);
        var userId = profiles.GetOrCreateByKey("owner").Id;

        Assert.Throws<ArgumentException>(() => profiles.UpdatePreferences(userId,
            new() { Theme = theme, Layout = layout }));
        database.ChangeTracker.Clear();

        Assert.Equal("classic", profiles.GetPreferences(userId).Theme);
        Assert.Equal("board", profiles.GetPreferences(userId).Layout);
    }

    [Theory]
    [InlineData("dog")]
    [InlineData("cat")]
    [InlineData("rabbit")]
    public void ChangingSpecies_PreservesNameAndEarnedProgress(string species)
    {
        using var database = Database();
        var user = Profiles(database).GetOrCreateByKey("pet-owner");
        var pets = Pets(database);
        pets.UpdateProfile(user.Id, new() { Name = "Mochi" });
        PetResponse before = pets.GetProfile(user.Id);
        for (var index = 0; index < 5; index++)
            before = pets.RewardForCompletedTask(user.UserKey);

        var updated = pets.UpdateProfile(user.Id, new() { Name = before.Name, Species = species });
        database.ChangeTracker.Clear();
        var stored = pets.GetProfile(user.Id);

        Assert.Equal(species, updated.Species);
        Assert.Equal(species, stored.Species);
        Assert.Equal("Mochi", stored.Name);
        Assert.Equal(before.Id, stored.Id);
        Assert.Equal(before.Level, stored.Level);
        Assert.Equal(before.Experience, stored.Experience);
        Assert.Equal(before.TotalExperience, stored.TotalExperience);
        Assert.Equal(before.CompletedTaskCount, stored.CompletedTaskCount);
        Assert.Equal(before.StreakDays, stored.StreakDays);
        Assert.Equal(before.Energy, stored.Energy);
        Assert.Equal(before.LastCompletedAt, stored.LastCompletedAt);
        Assert.Single(database.PetProfiles);
    }

    [Fact]
    public void RenamingPet_WithoutSpeciesPreservesPreviouslySelectedSpecies()
    {
        using var database = Database();
        var user = Profiles(database).GetOrCreateByKey("pet-owner");
        var pets = Pets(database);
        pets.UpdateProfile(user.Id, new() { Name = "Before", Species = "rabbit" });

        var renamed = pets.UpdateProfile(user.Id, new() { Name = "  After  " });
        database.ChangeTracker.Clear();

        Assert.Equal("After", renamed.Name);
        Assert.Equal("rabbit", renamed.Species);
        Assert.Equal("After", pets.GetProfile(user.Id).Name);
        Assert.Equal("rabbit", pets.GetProfile(user.Id).Species);
    }

    [Fact]
    public void DeletedUser_NumericPreferencesAndPetOperationsCannotRecreateAccount()
    {
        using var database = Database();
        var profiles = Profiles(database);
        var removedId = profiles.GetOrCreateByKey("removed").Id;
        Pets(database).UpdateProfile(removedId, new() { Name = "Removed pet", Species = "cat" });
        Assert.True(profiles.DeleteProfile(removedId));
        database.ChangeTracker.Clear();

        Action[] operations =
        [
            () => profiles.GetPreferences(removedId),
            () => profiles.UpdatePreferences(removedId, new() { Theme = "dark", Layout = "list" }),
            () => Pets(database).GetProfile(removedId),
            () => Pets(database).UpdateProfile(removedId, new() { Name = "Cannot return", Species = "rabbit" })
        ];
        foreach (var operation in operations)
            Assert.Equal(401, Assert.Throws<TeamOperationException>(operation).StatusCode);

        Assert.Empty(database.UserProfiles);
        Assert.Empty(database.PetProfiles);
    }
}

public sealed class PreferencesApiTests
{
    [Theory]
    [InlineData("preferences")]
    [InlineData("displayName")]
    public async Task StaleIdentityUpdate_CannotOverwriteSavedPreferencesOrDisplayName(string changedFields)
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        var registration = await factory.RegisterConfirmedAsync(client);
        using var staleScope = factory.Services.CreateScope();
        var staleManager = staleScope.ServiceProvider.GetRequiredService<UserManager<UserProfile>>();
        var staleUser = Assert.IsType<UserProfile>(
            await staleManager.FindByIdAsync(registration.User.Id.ToString()));
        var originalStamp = staleUser.ConcurrencyStamp;
        var originalLoginTime = staleUser.LastLoginAt;

        // A login/logout request can load this entity before another request saves options.
        using (var editingScope = factory.Services.CreateScope())
        {
            var profiles = editingScope.ServiceProvider.GetRequiredService<UserProfileService>();
            if (changedFields == "preferences")
                profiles.UpdatePreferences(staleUser.Id, new() { Theme = "dark", Layout = "list" });
            else
                profiles.UpdateProfile(staleUser.Id, new() { DisplayName = "Updated profile name" });
        }

        // UserManager updates the complete Identity entity, including its stale domain fields.
        staleUser.LastLoginAt = DateTime.UtcNow.AddMinutes(1);
        var result = await staleManager.UpdateAsync(staleUser);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ConcurrencyFailure");
        using var verificationScope = factory.Services.CreateScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await database.UserProfiles.AsNoTracking().SingleAsync(user => user.Id == staleUser.Id);
        Assert.NotEqual(originalStamp, stored.ConcurrencyStamp);
        Assert.Equal(originalLoginTime, stored.LastLoginAt);
        if (changedFields == "preferences")
        {
            Assert.Equal("dark", stored.Theme);
            Assert.Equal("list", stored.Layout);
        }
        else
        {
            Assert.Equal("Updated profile name", stored.DisplayName);
        }
    }

    [Fact]
    public async Task PreferencesAndPet_RequireAuthenticationAndConfirmedAccount()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        foreach (var path in new[] { "/api/user/preferences", "/api/pet" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);

        await client.RegisterAsync();
        foreach (var path in new[] { "/api/user/preferences", "/api/pet" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendWithCsrfAsync(HttpMethod.Put,
            "/api/user/preferences", new { theme = "dark", layout = "list" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendWithCsrfAsync(HttpMethod.Put,
            "/api/pet", new { name = "Unconfirmed", species = "cat" })).StatusCode);
    }

    [Fact]
    public async Task NewAccount_ReturnsDefaultPreferencesAndDogOverHttp()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);

        var preferences = await client.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences");
        var pet = await client.GetFromJsonAsync<PetResponse>("/api/pet");

        Assert.Equal("classic", Assert.IsType<UserPreferencesResponse>(preferences).Theme);
        Assert.Equal("board", preferences.Layout);
        Assert.Equal("dog", Assert.IsType<PetResponse>(pet).Species);
    }

    [Fact]
    public async Task PutPreferencesAndPet_ArePersistedAcrossLoginAndIsolatedFromOtherUsers()
    {
        using var factory = new TaskApiFactory();
        using var owner = factory.CreateSecureClient();
        using var other = factory.CreateSecureClient();
        var registration = await factory.RegisterConfirmedAsync(owner);
        var otherRegistration = await factory.RegisterConfirmedAsync(other);

        var preferencesResponse = await owner.SendWithCsrfAsync(HttpMethod.Put, "/api/user/preferences", new
        {
            theme = "dark", layout = "list", userId = otherRegistration.User.Id
        });
        Assert.Equal(HttpStatusCode.OK, preferencesResponse.StatusCode);
        var petResponse = await owner.SendWithCsrfAsync(HttpMethod.Put, "/api/pet", new
        {
            name = "Owner's cat", species = "cat", userId = otherRegistration.User.Id
        });
        Assert.Equal(HttpStatusCode.OK, petResponse.StatusCode);

        using var newBrowser = factory.CreateSecureClient();
        await newBrowser.LoginAsync(registration.User.UserKey);
        var preferences = Assert.IsType<UserPreferencesResponse>(
            await newBrowser.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences"));
        var pet = Assert.IsType<PetResponse>(await newBrowser.GetFromJsonAsync<PetResponse>("/api/pet"));
        Assert.Equal("dark", preferences.Theme);
        Assert.Equal("list", preferences.Layout);
        Assert.Equal("cat", pet.Species);
        Assert.Equal("Owner's cat", pet.Name);

        var otherPreferences = Assert.IsType<UserPreferencesResponse>(
            await other.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences"));
        var otherPet = Assert.IsType<PetResponse>(await other.GetFromJsonAsync<PetResponse>("/api/pet"));
        Assert.Equal("classic", otherPreferences.Theme);
        Assert.Equal("board", otherPreferences.Layout);
        Assert.Equal("dog", otherPet.Species);
        Assert.Equal("Task Pet", otherPet.Name);
        Assert.NotEqual(pet.Id, otherPet.Id);
    }

    [Fact]
    public async Task InvalidThemeLayoutAndSpecies_ReturnBadRequestWithoutChangingSavedValues()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);

        object[] invalidPreferences =
        [
            new { theme = "unknown", layout = "board" },
            new { theme = "Dark", layout = "board" },
            new { theme = "classic", layout = "grid" },
            new { theme = "classic", layout = "List" },
            new { theme = "", layout = "board" },
            new { theme = "light", layout = "" },
            new { theme = "light" },
            new { layout = "list" }
        ];
        foreach (var body in invalidPreferences)
        {
            var response = await client.SendWithCsrfAsync(HttpMethod.Put, "/api/user/preferences", body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }
        foreach (var species in new[] { "otter", "DOG", "../cat", " ", "" })
        {
            var response = await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet", new { name = "Invalid", species });
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                $"Species '{species}' returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        var preferences = Assert.IsType<UserPreferencesResponse>(
            await client.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences"));
        var pet = Assert.IsType<PetResponse>(await client.GetFromJsonAsync<PetResponse>("/api/pet"));
        Assert.Equal("classic", preferences.Theme);
        Assert.Equal("board", preferences.Layout);
        Assert.Equal("dog", pet.Species);
        Assert.Equal("Task Pet", pet.Name);
    }

    [Fact]
    public async Task PetName_IsRequiredAndSpeciesOmissionKeepsExistingSelection()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet",
            new { name = "First name", species = "rabbit" })).StatusCode);

        object[] invalidNames = [new { species = "cat" }, new { name = "", species = "cat" },
            new { name = "   ", species = "cat" }, new { name = new string('x', 101), species = "cat" }];
        foreach (var body in invalidNames)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet", body)).StatusCode);
        var response = await client.SendWithCsrfAsync(HttpMethod.Put, "/api/pet", new { name = "Second name" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = Assert.IsType<PetResponse>(await response.Content.ReadFromJsonAsync<PetResponse>());
        Assert.Equal("Second name", renamed.Name);
        Assert.Equal("rabbit", renamed.Species);
    }

    [Fact]
    public async Task Mutations_RequireCsrfAndDoNotWriteWhenHeaderIsMissing()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/user/preferences",
            new { theme = "dark", layout = "list" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/pet",
            new { name = "Unprotected", species = "cat" })).StatusCode);
        var preferences = Assert.IsType<UserPreferencesResponse>(
            await client.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences"));
        var pet = Assert.IsType<PetResponse>(await client.GetFromJsonAsync<PetResponse>("/api/pet"));
        Assert.Equal("classic", preferences.Theme);
        Assert.Equal("board", preferences.Layout);
        Assert.Equal("dog", pet.Species);
        Assert.Equal("Task Pet", pet.Name);
    }

    [Fact]
    public async Task DeletedAccount_OldCookieCannotRecreatePreferencesOrPetEvenIfUserKeyIsReused()
    {
        using var factory = new TaskApiFactory();
        using var owner = factory.CreateSecureClient();
        var original = await factory.RegisterConfirmedAsync(owner);
        var login = await owner.SendWithCsrfAsync(HttpMethod.Post, "/api/auth/login",
            new { userKey = original.User.UserKey, password = AuthTestRequests.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authCookies = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));

        // Keep replaying the original cookie even if a 401 response asks the browser to delete it.
        using var staleBrowser = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        staleBrowser.DefaultRequestHeaders.Add("Cookie", authCookies);
        Assert.Equal(HttpStatusCode.OK, (await staleBrowser.GetAsync("/api/user/preferences")).StatusCode);
        var csrfResponse = await staleBrowser.GetAsync("/api/auth/csrf");
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var csrfCookies = string.Join("; ", csrfResponse.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0]));
        staleBrowser.DefaultRequestHeaders.Remove("Cookie");
        staleBrowser.DefaultRequestHeaders.Add("Cookie", $"{authCookies}; {csrfCookies}");
        staleBrowser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.SendWithCsrfAsync(HttpMethod.Delete, "/api/user")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleBrowser.GetAsync("/api/user/preferences")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleBrowser.GetAsync("/api/pet")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleBrowser.PutAsJsonAsync("/api/user/preferences",
            new { theme = "dark", layout = "list" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleBrowser.PutAsJsonAsync("/api/pet",
            new { name = "Cannot return", species = "cat" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await database.UserProfiles.AnyAsync(user => user.Id == original.User.Id));
            Assert.False(await database.PetProfiles.AnyAsync(pet => pet.UserProfileId == original.User.Id));
        }

        using var replacementBrowser = factory.CreateSecureClient();
        var replacement = await factory.RegisterConfirmedAsync(replacementBrowser, original.User.UserKey);
        Assert.NotEqual(original.User.Id, replacement.User.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleBrowser.GetAsync("/api/user/preferences")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleBrowser.GetAsync("/api/pet")).StatusCode);
        var preferences = Assert.IsType<UserPreferencesResponse>(
            await replacementBrowser.GetFromJsonAsync<UserPreferencesResponse>("/api/user/preferences"));
        var pet = Assert.IsType<PetResponse>(await replacementBrowser.GetFromJsonAsync<PetResponse>("/api/pet"));
        Assert.Equal("classic", preferences.Theme);
        Assert.Equal("board", preferences.Layout);
        Assert.Equal("dog", pet.Species);
        Assert.Equal("Task Pet", pet.Name);
    }
}
