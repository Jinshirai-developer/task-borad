using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class TaskTagTests
{
    private static DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static AppDbContext Database() => new(Options());
    private static UserProfileService Users(AppDbContext db) => new(db, NullLogger<UserProfileService>.Instance);
    private static UserProfile User(AppDbContext db, string key = "owner") => Users(db).GetOrCreateByKey(key);
    private static TaskService Tasks(AppDbContext db) => new(db, NullLogger<TaskService>.Instance,
        new PetService(db, NullLogger<PetService>.Instance, Users(db)), Users(db));
    private static TaskItem SeedTask(AppDbContext db, int userId, string? tags, TaskItemStatus status = TaskItemStatus.Todo,
        int? teamId = null, string title = "Task", TaskPriority priority = TaskPriority.Medium)
    {
        var task = new TaskItem
        {
            UserProfileId = userId, TeamId = teamId, Tags = tags, Status = status, IsCompleted = status == TaskItemStatus.Done,
            Title = title, Priority = priority, CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Tasks.Add(task);
        db.SaveChanges();
        return task;
    }

    [Fact]
    public void RegisterEmptyCategory_TrimsNameAndPersistsAfterItsLastTaskIsDeleted()
    {
        using var db = Database();
        var user = User(db);
        var tags = new TaskTagService(db);
        var result = tags.Create(user.Id, new() { Name = " プログラマー " });
        Assert.Equal("プログラマー", result.Name);
        var initial = tags.GetAll(user.Id);
        Assert.Equal(0, initial.TotalTasks);
        Assert.Equal(0, Assert.Single(initial.Items).Total);
        Assert.Equal(1, initial.RegisteredTags);
        Assert.Equal(50, initial.MaxRegisteredTags);
        var task = Tasks(db).Create(user.Id, new() { Title = "Temporary task", Tags = result.Name });
        Assert.Equal(1, tags.GetAll(user.Id).Items.Single().Total);
        Tasks(db).Delete(user.Id, task.Id);

        Assert.Equal(0, Assert.Single(tags.GetAll(user.Id).Items).Total);
        Assert.Single(db.TaskTagDefinitions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("art,programming")]
    [InlineData("art\nplanner")]
    [InlineData("\tprogrammer")]
    [InlineData("art\0planner")]
    public void Register_RejectsEmptyCommaAndControlCharacters(string? name)
    {
        using var db = Database();
        var user = User(db);
        var error = Assert.Throws<TeamOperationException>(() => new TaskTagService(db).Create(user.Id, new() { Name = name! }));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("invalid_tag_name", error.Code);
        Assert.Empty(db.TaskTagDefinitions);
    }

    [Fact]
    public void Register_AllowsFiftyCharactersAfterTrimButRejectsFiftyOne()
    {
        using var db = Database();
        var user = User(db);
        var tags = new TaskTagService(db);
        var name = new string('あ', 50);
        Assert.Equal(name, tags.Create(user.Id, new() { Name = $"  {name}  " }).Name);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => tags.Create(user.Id, new() { Name = new string('a', 51) })).StatusCode);
        Assert.Single(db.TaskTagDefinitions);
    }

    [Fact]
    public void RegisteredNames_AreCaseInsensitiveWithinScopeButIndependentAcrossScopes()
    {
        using var db = Database();
        var owner = User(db);
        var other = User(db, "other");
        var team = new TeamService(db).Create(owner.Id, new() { Name = "Team" });
        var tags = new TaskTagService(db);
        tags.Create(owner.Id, new() { Name = "Programmer" });
        Assert.Equal("tag_already_exists", Assert.Throws<TeamOperationException>(() =>
            tags.Create(owner.Id, new() { Name = " programmer " })).Code);
        tags.Create(other.Id, new() { Name = "programmer" });
        tags.Create(owner.Id, new() { Name = "PROGRAMMER" }, team.Team.Id);

        Assert.Equal(3, db.TaskTagDefinitions.Count());
        Assert.Equal("Programmer", Assert.Single(tags.GetAll(owner.Id).Items).Name);
        Assert.Equal("PROGRAMMER", Assert.Single(tags.GetAll(owner.Id, team.Team.Id).Items).Name);
    }

    [Fact]
    public void Catalog_UnionsRegisteredAndLegacyTagsCountsEachTaskOnceAndNeverRewritesCsv()
    {
        using var db = Database();
        var user = User(db);
        var tags = new TaskTagService(db);
        tags.Create(user.Id, new() { Name = "Programmer" });
        tags.Create(user.Id, new() { Name = "Planner" });
        var longName = new string('l', 140);
        SeedTask(db, user.Id, " programmer, PROGRAMMER , Artist ");
        SeedTask(db, user.Id, "PROGRAMMER, artist", TaskItemStatus.Doing);
        SeedTask(db, user.Id, "ARTIST", TaskItemStatus.Done);
        SeedTask(db, user.Id, " , , \t");
        SeedTask(db, user.Id, null, TaskItemStatus.Doing);
        SeedTask(db, user.Id, longName);
        var original = db.Tasks.OrderBy(task => task.Id).Select(task => task.Tags).ToArray();

        var catalog = tags.GetAll(user.Id);

        Assert.Equal(6, catalog.TotalTasks);
        Assert.Equal(2, catalog.UntaggedTasks);
        Assert.Equal(2, catalog.RegisteredTags);
        Assert.Equal(4, catalog.Items.Count);
        var programmer = catalog.Items.Single(item => item.Name == "Programmer");
        Assert.Equal((2, 1, 1, 0), (programmer.Total, programmer.Todo, programmer.Doing, programmer.Done));
        var artist = catalog.Items.Single(item => item.Name == "Artist");
        Assert.Equal((3, 1, 1, 1), (artist.Total, artist.Todo, artist.Doing, artist.Done));
        Assert.Equal(0, catalog.Items.Single(item => item.Name == "Planner").Total);
        Assert.Equal(1, catalog.Items.Single(item => item.Name == longName).Total);
        Assert.Equal(original, db.Tasks.OrderBy(task => task.Id).Select(task => task.Tags).ToArray());
        Assert.Equal(2, db.TaskTagDefinitions.Count());
        Assert.Empty(db.CompletionRewards);
        Assert.Empty(db.PetProfiles);
    }

    [Fact]
    public void LegacyTagCanBeExplicitlyRegisteredWithoutDuplicatingCatalogItem()
    {
        using var db = Database();
        var user = User(db);
        var tags = new TaskTagService(db);
        SeedTask(db, user.Id, "artist");
        Assert.Equal(0, tags.GetAll(user.Id).RegisteredTags);

        tags.Create(user.Id, new() { Name = "Artist" });

        var catalog = tags.GetAll(user.Id);
        Assert.Equal(1, catalog.RegisteredTags);
        Assert.Equal("Artist", Assert.Single(catalog.Items).Name);
        Assert.Equal(1, catalog.Items.Single().Total);
        Assert.Equal("artist", db.Tasks.Single().Tags);
    }

    [Fact]
    public void LegacyUnionDoesNotConsumeRegisteredCategoryQuota()
    {
        using var db = Database();
        var user = User(db);
        SeedTask(db, user.Id, string.Join(',', Enumerable.Range(0, 51).Select(index => $"t{index}")));
        var tags = new TaskTagService(db);
        Assert.Equal(51, tags.GetAll(user.Id).Items.Count);
        for (var index = 0; index < 50; index++) tags.Create(user.Id, new() { Name = $"registered-{index}" });

        var catalog = tags.GetAll(user.Id);
        Assert.Equal(101, catalog.Items.Count);
        Assert.Equal(50, catalog.RegisteredTags);
        Assert.Equal("tag_limit", Assert.Throws<TeamOperationException>(() => tags.Create(user.Id, new() { Name = "one-too-many" })).Code);
    }

    [Fact]
    public void ExactTagFilter_UsesWholeCaseInsensitiveTokensBeforePaging()
    {
        using var db = Database();
        var user = User(db);
        SeedTask(db, user.Id, "Art");
        SeedTask(db, user.Id, "Artist");
        SeedTask(db, user.Id, " Cart, ART,art ");
        SeedTask(db, user.Id, "Art Direction");
        var lastExact = SeedTask(db, user.Id, "programmer, aRt");

        var result = Tasks(db).GetAll(user.Id, null, null, null, null, "asc", null, 2, 2, tagExact: " ART ");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(lastExact.Id, Assert.Single(result.Items).Id);
        Assert.Equal(5, new TaskTagService(db).GetAll(user.Id).TotalTasks);
    }

    [Fact]
    public void ExactTagFilter_PreservesThreeHundredCharacterLegacyNames()
    {
        using var db = Database();
        var user = User(db);
        var name = new string('a', 300);
        var task = SeedTask(db, user.Id, name);

        var result = Tasks(db).GetAll(user.Id, null, null, null, null, null, null, 1, 10, tagExact: name.ToUpperInvariant());

        Assert.Equal(task.Id, Assert.Single(result.Items).Id);
        Assert.Equal(name, Assert.Single(new TaskTagService(db).GetAll(user.Id).Items).Name);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() =>
            Tasks(db).GetAll(user.Id, null, null, null, null, null, null, 1, 10, tagExact: new string('a', 301))).StatusCode);
    }

    [Fact]
    public void UntaggedFilter_IncludesNullWhitespaceAndCommaOnlyCsv()
    {
        using var db = Database();
        var user = User(db);
        foreach (var value in new string?[] { null, "", "   ", ",,,", " , \t, " }) SeedTask(db, user.Id, value);
        SeedTask(db, user.Id, "Art");
        var tasks = Tasks(db);

        var result = tasks.GetAll(user.Id, null, null, null, null, "asc", null, 2, 2, untagged: true);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, new TaskTagService(db).GetAll(user.Id).UntaggedTasks);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() =>
            tasks.GetAll(user.Id, null, null, null, null, null, null, 1, 10, tagExact: "Art", untagged: true)).StatusCode);
    }

    [Fact]
    public void ExactFilter_CombinesWithLegacyPartialSearchAndOtherTaskFilters()
    {
        using var db = Database();
        var user = User(db);
        var match = SeedTask(db, user.Id, "Art, urgent", TaskItemStatus.Doing, title: "Match", priority: TaskPriority.High);
        SeedTask(db, user.Id, "Artist, urgent", TaskItemStatus.Doing, title: "Match", priority: TaskPriority.High);
        SeedTask(db, user.Id, "Art, urgent", TaskItemStatus.Doing, title: "Other", priority: TaskPriority.High);
        SeedTask(db, user.Id, "Art, urgent", TaskItemStatus.Todo, title: "Match", priority: TaskPriority.High);

        var result = Tasks(db).GetAll(user.Id, null, TaskItemStatus.Doing, TaskPriority.High,
            "urge", null, "Match", 1, 10, tagExact: "ART");

        Assert.Equal(match.Id, Assert.Single(result.Items).Id);
        Assert.Equal(4, new TaskTagService(db).GetAll(user.Id).TotalTasks);
    }

    [Fact]
    public void ScopeIsolation_KeepsTagsCountsAndFiltersPrivateAndRevokesLeavingMembersAccess()
    {
        using var db = Database();
        var owner = User(db);
        var member = User(db, "member");
        var outsider = User(db, "outsider");
        var teams = new TeamService(db);
        var team = teams.Create(owner.Id, new() { Name = "First" });
        var second = teams.Create(owner.Id, new() { Name = "Second" });
        teams.Join(member.Id, new() { InviteCode = team.InviteCode });
        var tags = new TaskTagService(db);
        tags.Create(owner.Id, new() { Name = "private-only" });
        tags.Create(member.Id, new() { Name = "team-only" }, team.Team.Id);
        tags.Create(owner.Id, new() { Name = "other-team" }, second.Team.Id);
        SeedTask(db, owner.Id, "Artist");
        var shared = SeedTask(db, owner.Id, "Artist", teamId: team.Team.Id);
        SeedTask(db, owner.Id, "Artist", teamId: second.Team.Id);

        Assert.Equal(1, tags.GetAll(member.Id, team.Team.Id).TotalTasks);
        Assert.DoesNotContain(tags.GetAll(member.Id, team.Team.Id).Items, item => item.Name == "private-only" || item.Name == "other-team");
        Assert.Empty(tags.GetAll(member.Id).Items);
        Assert.Equal(shared.Id, Assert.Single(Tasks(db).GetAll(member.Id, null, null, null, null, null, null, 1, 10,
            team.Team.Id, tagExact: "Artist").Items).Id);
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => tags.GetAll(outsider.Id, team.Team.Id)).StatusCode);
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => tags.Create(outsider.Id, new() { Name = "Forbidden" }, team.Team.Id)).StatusCode);
        teams.Leave(member.Id, team.Team.Id);
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => tags.GetAll(member.Id, team.Team.Id)).StatusCode);
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => tags.Create(member.Id, new() { Name = "Forbidden" }, team.Team.Id)).StatusCode);
    }

    [Fact]
    public void AccountAndTeamDeletion_RemoveOnlyTheirTagDefinitionsInFreshContexts()
    {
        var options = Options();
        int ownerId, memberId, teamId;
        using (var db = new AppDbContext(options))
        {
            ownerId = User(db).Id;
            memberId = User(db, "member").Id;
            var team = new TeamService(db).Create(ownerId, new() { Name = "Team" });
            teamId = team.Team.Id;
            new TeamService(db).Join(memberId, new() { InviteCode = team.InviteCode });
            var tags = new TaskTagService(db);
            tags.Create(ownerId, new() { Name = "Owner private" });
            tags.Create(memberId, new() { Name = "Member private" });
            tags.Create(memberId, new() { Name = "Shared survives member deletion" }, teamId);
        }
        using (var deletion = new AppDbContext(options)) Users(deletion).DeleteProfile(memberId);
        using (var check = new AppDbContext(options))
        {
            Assert.Equal(2, check.TaskTagDefinitions.Count());
            Assert.Equal(1, new TaskTagService(check).GetAll(ownerId, teamId).RegisteredTags);
        }
        using (var deletion = new AppDbContext(options)) new TeamService(deletion).Delete(ownerId, teamId);
        using var final = new AppDbContext(options);
        Assert.Equal("Owner private", Assert.Single(final.TaskTagDefinitions).Name);
        Assert.Equal(401, Assert.Throws<TeamOperationException>(() =>
            new TaskTagService(final).Create(memberId, new() { Name = "No recreation" })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRegistration_EnforcesFinalQuotaSlot()
    {
        var options = Options();
        int userId;
        using (var db = new AppDbContext(options))
        {
            userId = User(db).Id;
            var tags = new TaskTagService(db);
            for (var index = 0; index < 49; index++) tags.Create(userId, new() { Name = $"Existing {index}" });
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => Task.Run(() =>
        {
            using var db = new AppDbContext(options);
            try { new TaskTagService(db).Create(userId, new() { Name = $"Candidate {index}" }); return "created"; }
            catch (TeamOperationException error) { return error.Code; }
        })));

        Assert.Equal(1, outcomes.Count(outcome => outcome == "created"));
        Assert.Equal(5, outcomes.Count(outcome => outcome == "tag_limit"));
        using var check = new AppDbContext(options);
        Assert.Equal(50, check.TaskTagDefinitions.Count());
    }
}

public sealed class TaskTagApiTests
{
    [Fact]
    public async Task TagRoutes_RequireCsrfAndMemberAccessAndIgnoreBodyOrQueryScopeIds()
    {
        using var factory = new TaskApiFactory();
        using var owner = factory.CreateSecureClient();
        using var member = factory.CreateSecureClient();
        using var outsider = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/task-tags")).StatusCode);
        await factory.RegisterConfirmedAsync(owner);
        await factory.RegisterConfirmedAsync(member);
        await factory.RegisterConfirmedAsync(outsider);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/task-tags", new { name = "No CSRF" })).StatusCode);
        var created = await owner.SendWithCsrfAsync(HttpMethod.Post, "/api/task-tags", new { name = "Programmer" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Programmer", (await created.Content.ReadFromJsonAsync<TaskTagCreatedResponse>())!.Name);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.SendWithCsrfAsync(HttpMethod.Post, "/api/task-tags", new { name = "programmer" })).StatusCode);
        var teamCreation = await owner.SendWithCsrfAsync(HttpMethod.Post, "/api/teams", new { name = "Tag team" });
        var team = (await teamCreation.Content.ReadFromJsonAsync<TeamCreatedResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await member.SendWithCsrfAsync(HttpMethod.Post, "/api/teams/join", new { inviteCode = team.InviteCode })).StatusCode);
        var path = $"/api/teams/{team.Team.Id}/task-tags";
        Assert.Equal(HttpStatusCode.Created, (await member.SendWithCsrfAsync(HttpMethod.Post, path, new { name = "Artist" })).StatusCode);
        Assert.Equal("Artist", Assert.Single((await owner.GetFromJsonAsync<TaskTagCatalogResponse>(path))!.Items).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendWithCsrfAsync(HttpMethod.Post, path, new { name = "Forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await member.SendWithCsrfAsync(HttpMethod.Post, $"/api/task-tags?teamId={team.Team.Id}",
            new { name = "Member private", teamId = team.Team.Id })).StatusCode);
        Assert.Equal("Member private", Assert.Single((await member.GetFromJsonAsync<TaskTagCatalogResponse>("/api/task-tags"))!.Items).Name);
        Assert.Equal(1, (await member.GetFromJsonAsync<TaskTagCatalogResponse>(path))!.RegisteredTags);
    }

    [Fact]
    public async Task ExactAndUntaggedQueries_FilterBeforePagingAndRejectConflictingOrOversizedInput()
    {
        using var factory = new TaskApiFactory();
        using var client = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);
        foreach (var tags in new string?[] { "program", "programmer", "Programmer, Artist", null })
            Assert.Equal(HttpStatusCode.Created, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new { title = "Tagged task", tags })).StatusCode);
        var exact = await client.GetFromJsonAsync<TaggedPage>("/api/tasks?tagExact=PROGRAMMER&pageSize=1&page=2");
        Assert.Equal(2, exact!.TotalCount);
        Assert.Equal(2, exact.TotalPages);
        Assert.Single(exact.Items);
        var untagged = await client.GetFromJsonAsync<TaggedPage>("/api/tasks?untagged=true");
        Assert.Equal(1, untagged!.TotalCount);
        Assert.Null(Assert.Single(untagged.Items).Tags);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tasks?tagExact=programmer&untagged=true")).StatusCode);
        var emptyExact = await client.GetAsync("/api/tasks?tagExact=");
        Assert.Equal(HttpStatusCode.BadRequest, emptyExact.StatusCode);
        Assert.Equal("invalid_exact_tag", (await emptyExact.Content.ReadFromJsonAsync<TagErrorPayload>())!.Code);
        var emptyCombined = await client.GetAsync("/api/tasks?tagExact=&untagged=true");
        Assert.Equal(HttpStatusCode.BadRequest, emptyCombined.StatusCode);
        Assert.Equal("invalid_tag_filters", (await emptyCombined.Content.ReadFromJsonAsync<TagErrorPayload>())!.Code);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/tasks?tagExact={new string('a', 301)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tasks?untagged=invalid")).StatusCode);
        var catalog = await client.GetFromJsonAsync<TaskTagCatalogResponse>("/api/task-tags?page=9&search=nonmatching");
        Assert.Equal(4, catalog!.TotalTasks);
        Assert.Equal(0, catalog.RegisteredTags);
        Assert.Equal(1, catalog.UntaggedTasks);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWithCsrfAsync(HttpMethod.Post, "/api/task-tags", new { name = new string('a', 51) })).StatusCode);
    }

    private sealed record TaggedPage(List<TaggedTask> Items, int TotalCount, int TotalPages);
    private sealed record TaggedTask(string? Tags);
    private sealed record TagErrorPayload(string Code);
}
