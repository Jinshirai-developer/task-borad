using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class TeamTests
{
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static UserProfile User(AppDbContext database, string key) => Profiles(database).GetOrCreateByKey(key);
    private static UserProfileService Profiles(AppDbContext database) => new(database, NullLogger<UserProfileService>.Instance);
    private static TaskService Tasks(AppDbContext database) => new(database, NullLogger<TaskService>.Instance,
        new PetService(database, NullLogger<PetService>.Instance, Profiles(database)), Profiles(database));

    [Fact]
    public void Create_StoresOnlyHashedInviteAndAddsOwnerMembership()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var service = new TeamService(database);
        var created = service.Create(owner.Id, new CreateTeamRequest { Name = "  Development  " });

        Assert.Equal("Development", created.Team.Name);
        Assert.Equal("owner", created.Team.Role);
        Assert.Equal(owner.Id, Assert.Single(created.Team.Members).UserProfileId);
        Assert.Equal(43, created.InviteCode.Length);
        Assert.NotEqual(created.InviteCode, Assert.Single(database.Teams).InviteCodeHash);
        Assert.InRange(created.ExpiresAt, DateTime.UtcNow.AddDays(6), DateTime.UtcNow.AddDays(8));
        Assert.Single(service.GetAll(owner.Id));
    }

    [Fact]
    public void Invite_RotationAndExpiryRejectPreviousCodes()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var member = User(database, "member");
        var service = new TeamService(database);
        var created = service.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        var replacement = service.RotateInvite(owner.Id, created.Team.Id);

        Assert.Throws<TeamOperationException>(() => service.Join(member.Id, new JoinTeamRequest { InviteCode = created.InviteCode }));
        Assert.Equal("member", service.Join(member.Id, new JoinTeamRequest { InviteCode = replacement.InviteCode }).Role);
        Assert.Equal(2, service.Join(member.Id, new JoinTeamRequest { InviteCode = replacement.InviteCode }).MemberCount);
        database.Teams.Single().InviteExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        database.SaveChanges();
        Assert.Equal("invalid_invite", Assert.Throws<TeamOperationException>(() => service.Join(member.Id,
            new JoinTeamRequest { InviteCode = replacement.InviteCode })).Code);
    }

    [Fact]
    public void Tasks_RequireExplicitScopeAndDoNotLeakPrivateOrOtherTeams()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var member = User(database, "member");
        var outsider = User(database, "outsider");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "First" });
        var other = teams.Create(owner.Id, new CreateTeamRequest { Name = "Second" });
        teams.Join(member.Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        var personal = tasks.Create(owner.Id, new CreateTaskRequest { Title = "Private" });
        var shared = tasks.Create(owner.Id, new CreateTaskRequest { Title = "Shared" }, team.Team.Id);
        var second = tasks.Create(owner.Id, new CreateTaskRequest { Title = "Second" }, other.Team.Id);

        Assert.Equal(personal.Id, Assert.Single(tasks.GetAll(owner.Id, null, null, null, null, null, null, 1, 10).Items).Id);
        Assert.Equal(shared.Id, Assert.Single(tasks.GetAll(member.Id, null, null, null, null, null, null, 1, 10, team.Team.Id).Items).Id);
        Assert.Null(tasks.GetById(owner.Id, shared.Id));
        Assert.Null(tasks.GetById(member.Id, personal.Id, team.Team.Id));
        Assert.Null(tasks.GetById(owner.Id, second.Id, team.Team.Id));
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => tasks.GetById(outsider.Id, shared.Id, team.Team.Id)).StatusCode);
        Assert.Null(tasks.Update(owner.Id, shared.Id, new UpdateTaskRequest { Title = "Wrong route" }));
        Assert.False(tasks.Delete(owner.Id, shared.Id));
    }

    [Fact]
    public void SharedTask_RequiresCurrentVersionAndRewardsCurrentCompleterOnlyOnce()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var member = User(database, "member");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        teams.Join(member.Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        var shared = tasks.Create(owner.Id, new CreateTaskRequest { Title = "Shared" }, team.Team.Id);

        Assert.Throws<DbUpdateConcurrencyException>(() => tasks.Update(member.Id, shared.Id,
            new UpdateTaskRequest { Title = "Missing version" }, team.Team.Id));
        Assert.Throws<DbUpdateConcurrencyException>(() => tasks.Update(member.Id, shared.Id,
            new UpdateTaskRequest { Title = "Stale version", Version = shared.Version + 1 }, team.Team.Id));
        Assert.Throws<DbUpdateConcurrencyException>(() => tasks.Delete(member.Id, shared.Id, teamId: team.Team.Id));

        var done = tasks.Update(member.Id, shared.Id, new UpdateTaskRequest
        { Title = "Done", Status = TaskItemStatus.Done, Version = shared.Version }, team.Team.Id)!;
        tasks.Update(owner.Id, shared.Id, new UpdateTaskRequest
        { Title = "Reopened", Status = TaskItemStatus.Todo, Version = done.Version }, team.Team.Id);
        Assert.Equal(0, database.PetProfiles.Single(pet => pet.UserProfileId == member.Id).TotalExperience);
        tasks.Update(owner.Id, shared.Id, new UpdateTaskRequest
        { Title = "Done again", Status = TaskItemStatus.Done, Version = done.Version }, team.Team.Id);

        var pet = database.PetProfiles.Single(profile => profile.UserProfileId == owner.Id);
        Assert.Equal(25, pet.TotalExperience);
        Assert.Equal(1, pet.CompletedTaskCount);
        Assert.Equal(25, database.PetProfiles.Sum(profile => profile.TotalExperience));
        Assert.Equal(owner.Id, Assert.Single(database.CompletionRewards).UserProfileId);
        Assert.True(tasks.Delete(member.Id, shared.Id, done.Version, team.Team.Id));
    }

    [Fact]
    public void Summary_CountsWholeTeamAndExcludesPrivateAndCompletedOverdueTasks()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        var tasks = Tasks(database);
        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Personal", DueDate = DateTime.UtcNow.AddDays(-1) });
        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Todo", DueDate = DateTime.UtcNow.AddDays(-1) }, team.Team.Id);
        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Doing", Status = TaskItemStatus.Doing }, team.Team.Id);
        tasks.Create(owner.Id, new CreateTaskRequest
        { Title = "Done", Status = TaskItemStatus.Done, DueDate = DateTime.UtcNow.AddDays(-1) }, team.Team.Id);

        Assert.Equal(new TeamSummaryResponse(3, 1, 1, 1, 1), teams.GetSummary(owner.Id, team.Team.Id));
    }

    [Fact]
    public void Summary_DateOnlyDeadlinesUseJapaneseCalendarAndExcludeToday()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        var tasks = Tasks(database);
        var todayInJapan = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9).Date, DateTimeKind.Utc);
        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Due today", DueDate = todayInJapan }, team.Team.Id);

        Assert.Equal(0, teams.GetSummary(owner.Id, team.Team.Id).Overdue);

        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Due yesterday", DueDate = todayInJapan.AddDays(-1) }, team.Team.Id);

        Assert.Equal(1, teams.GetSummary(owner.Id, team.Team.Id).Overdue);
    }

    [Fact]
    public void Ownership_TransferRequiresMemberRevokesInviteAndAllowsFormerOwnerToLeave()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var member = User(database, "member");
        var outsider = User(database, "outsider");
        var service = new TeamService(database);
        var created = service.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        service.Join(member.Id, new JoinTeamRequest { InviteCode = created.InviteCode });

        Assert.Equal("team_owner", Assert.Throws<TeamOperationException>(() => service.Leave(owner.Id, created.Team.Id)).Code);
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => service.Delete(member.Id, created.Team.Id)).StatusCode);
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => service.RotateInvite(member.Id, created.Team.Id)).StatusCode);
        Assert.Throws<TeamOperationException>(() => service.TransferOwner(owner.Id, created.Team.Id, outsider.Id));
        service.TransferOwner(owner.Id, created.Team.Id, member.Id);
        Assert.Throws<TeamOperationException>(() => service.Join(outsider.Id, new JoinTeamRequest { InviteCode = created.InviteCode }));
        service.Leave(owner.Id, created.Team.Id);
        Assert.Empty(service.GetAll(owner.Id));
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => service.Get(owner.Id, created.Team.Id)).StatusCode);
        Assert.Equal("owner", service.Get(member.Id, created.Team.Id).Role);
    }

    [Fact]
    public void Leaving_ImmediatelyRemovesSharedTaskAccess()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var member = User(database, "member");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        teams.Join(member.Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        var shared = tasks.Create(member.Id, new CreateTaskRequest { Title = "Still shared after leaving" }, team.Team.Id);
        teams.Leave(member.Id, team.Team.Id);

        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => tasks.Update(member.Id, shared.Id,
            new UpdateTaskRequest { Title = "Forbidden", Version = shared.Version }, team.Team.Id)).StatusCode);
        Assert.NotNull(tasks.GetById(owner.Id, shared.Id, team.Team.Id));
    }

    [Fact]
    public void MembershipAndTeamSizeLimits_AreEnforced()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var teams = new TeamService(database);
        for (var index = 0; index < TeamService.MaximumMemberships; index++)
            teams.Create(owner.Id, new CreateTeamRequest { Name = $"Team {index}" });
        Assert.Equal("membership_limit", Assert.Throws<TeamOperationException>(() =>
            teams.Create(owner.Id, new CreateTeamRequest { Name = "Over quota" })).Code);
        var secondOwner = User(database, "second-owner");
        var team = teams.Create(secondOwner.Id, new CreateTeamRequest { Name = "Full team" });
        for (var index = 1; index < TeamService.MaximumMembers; index++)
            teams.Join(User(database, $"member-{index}").Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        Assert.Equal("team_full", Assert.Throws<TeamOperationException>(() =>
            teams.Join(User(database, "overflow").Id, new JoinTeamRequest { InviteCode = team.InviteCode })).Code);
    }

    [Fact]
    public void SharedTaskQuota_IsSeparateFromPersonalQuota()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var team = new TeamService(database).Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        database.Tasks.AddRange(Enumerable.Range(1, 500).Select(index =>
            new TaskItem { Title = $"Task {index}", TeamId = team.Team.Id, UserProfileId = owner.Id }));
        database.SaveChanges();
        var tasks = Tasks(database);

        Assert.Throws<TaskLimitExceededException>(() => tasks.Create(owner.Id, new CreateTaskRequest { Title = "Over quota" }, team.Team.Id));
        Assert.Null(tasks.Create(owner.Id, new CreateTaskRequest { Title = "Personal allowed" }).TeamId);
    }

    [Fact]
    public void DeleteTeam_RemovesOnlyItsSharedTasksAndMemberships()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        var tasks = Tasks(database);
        var personal = tasks.Create(owner.Id, new CreateTaskRequest { Title = "Personal" });
        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Shared" }, team.Team.Id);

        teams.Delete(owner.Id, team.Team.Id);
        Assert.Empty(database.Teams);
        Assert.Empty(database.TeamMembers);
        Assert.Equal(personal.Id, Assert.Single(database.Tasks).Id);
    }

    [Fact]
    public void DeletedAccount_NumericTaskApiNeverRecreatesUser()
    {
        using var database = Database();
        var removed = User(database, "removed");
        var removedId = removed.Id;
        Profiles(database).DeleteProfile(removed.UserKey);

        var error = Assert.Throws<TeamOperationException>(() => Tasks(database).Create(removedId,
            new CreateTaskRequest { Title = "Cannot recreate account" }));
        Assert.Equal(401, error.StatusCode);
        Assert.Empty(database.UserProfiles);
    }

    [Fact]
    public void AccountDeletion_BlocksOwnerButPreservesDepartingMembersSharedTasks()
    {
        using var database = Database();
        var owner = User(database, "owner");
        var member = User(database, "member");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Team" });
        teams.Join(member.Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        tasks.Create(member.Id, new CreateTaskRequest { Title = "Private" });
        var shared = tasks.Create(member.Id, new CreateTaskRequest { Title = "Shared" }, team.Team.Id);

        Assert.Equal("team_owner", Assert.Throws<TeamOperationException>(() => Profiles(database).DeleteProfile(owner.UserKey)).Code);
        Assert.True(Profiles(database).DeleteProfile(member.UserKey));
        var retained = Assert.Single(database.Tasks);
        Assert.Equal(shared.Id, retained.Id);
        Assert.Null(retained.UserProfileId);
        Assert.Equal("退会したユーザー", tasks.GetById(owner.Id, shared.Id, team.Team.Id)!.CreatedByDisplayName);
        Assert.Single(database.TeamMembers);
    }
}

public sealed class TeamApiTests
{
    [Fact]
    public async Task Routes_EnforceScopeMembershipVersionAndCsrf()
    {
        using var factory = new TaskApiFactory();
        using var owner = factory.CreateSecureClient();
        using var member = factory.CreateSecureClient();
        using var outsider = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(owner);
        await factory.RegisterConfirmedAsync(member);
        await factory.RegisterConfirmedAsync(outsider);
        var create = await owner.SendWithCsrfAsync(HttpMethod.Post, "/api/teams", new { name = "HTTP team" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var team = (await create.Content.ReadFromJsonAsync<TeamCreatedResponse>())!;
        Assert.DoesNotContain("inviteCodeHash", await create.Content.ReadAsStringAsync());
        var join = await member.SendWithCsrfAsync(HttpMethod.Post, "/api/teams/join", new { team.InviteCode });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var taskPath = $"/api/teams/{team.Team.Id}/tasks";
        var created = await owner.SendWithCsrfAsync(HttpMethod.Post, taskPath, new { title = "Shared HTTP task" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = (await created.Content.ReadFromJsonAsync<TaskVersionPayload>())!;
        Assert.Equal(team.Team.Id, task.TeamId);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{taskPath}/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/teams/{team.Team.Id}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync(taskPath, new { title = "No CSRF" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await member.SendWithCsrfAsync(HttpMethod.Put, $"{taskPath}/{task.Id}", new { title = "No version" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await member.SendWithCsrfAsync(HttpMethod.Delete, $"{taskPath}/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.SendWithCsrfAsync(HttpMethod.Put, $"{taskPath}/{task.Id}",
            new { title = "Updated by member", version = task.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.SendWithCsrfAsync(HttpMethod.Delete, $"{taskPath}/{task.Id}?version={task.Version}")).StatusCode);
    }

    [Fact]
    public async Task QueryAndBodyTeamIds_DoNotChangePrivateRouteScope()
    {
        using var factory = new TaskApiFactory();
        using var owner = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(owner);
        var response = await owner.SendWithCsrfAsync(HttpMethod.Post, "/api/teams", new { name = "HTTP team" });
        var team = (await response.Content.ReadFromJsonAsync<TeamCreatedResponse>())!;
        var created = await owner.SendWithCsrfAsync(HttpMethod.Post, $"/api/tasks?teamId={team.Team.Id}",
            new { title = "Still private", teamId = team.Team.Id });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = (await created.Content.ReadFromJsonAsync<TaskVersionPayload>())!;
        Assert.Null(task.TeamId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/teams/{team.Team.Id}/tasks/{task.Id}")).StatusCode);
    }

    private sealed record TaskVersionPayload(int Id, int? TeamId, uint Version);
}
