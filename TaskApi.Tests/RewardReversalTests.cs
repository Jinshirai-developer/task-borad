using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class RewardReversalTests
{
    private static DbContextOptions<AppDbContext> DatabaseOptions() => new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static AppDbContext Database() => new(DatabaseOptions());
    private static UserProfileService Users(AppDbContext database) => new(database, NullLogger<UserProfileService>.Instance);
    private static UserProfile User(AppDbContext database, string key = "owner") => Users(database).GetOrCreateByKey(key);
    private static PetService Pets(AppDbContext database) => new(database, NullLogger<PetService>.Instance, Users(database));
    private static TaskService Tasks(AppDbContext database) => new(database, NullLogger<TaskService>.Instance, Pets(database), Users(database));
    private static PetProfile Pet(AppDbContext database, int userId) => database.PetProfiles.Single(pet => pet.UserProfileId == userId);
    private static TaskResponse Change(TaskService tasks, int userId, TaskResponse task, TaskItemStatus status, int? teamId = null) =>
        tasks.Update(userId, task.Id, new UpdateTaskRequest { Title = task.Title, Status = status, Version = task.Version }, teamId)!;

    [Theory]
    [InlineData(TaskItemStatus.Todo)]
    [InlineData(TaskItemStatus.Doing)]
    public void Reopen_ReversesXpCountEnergyAndActivity(TaskItemStatus nextStatus)
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Done", Status = TaskItemStatus.Done });
        Assert.Equal(25, Pet(database, user.Id).TotalExperience);
        Assert.Equal(92, Pet(database, user.Id).Energy);

        Change(tasks, user.Id, task, nextStatus);

        var pet = Pet(database, user.Id);
        Assert.Equal(0, pet.TotalExperience);
        Assert.Equal(0, pet.Experience);
        Assert.Equal(1, pet.Level);
        Assert.Equal(0, pet.CompletedTaskCount);
        Assert.Equal(80, pet.Energy);
        Assert.Equal(0, pet.StreakDays);
        Assert.Null(pet.LastCompletedAt);
        Assert.Null(database.Tasks.Single().CompletionRewardedAt);
        Assert.NotNull(Assert.Single(database.CompletionRewards).RevokedAt);
    }

    [Fact]
    public void TodoDoingTransitions_DoNotGrantOrRevokeRewards()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Pending" });
        task = Change(tasks, user.Id, task, TaskItemStatus.Doing);
        Change(tasks, user.Id, task, TaskItemStatus.Todo);

        Assert.Empty(database.CompletionRewards);
        Assert.Empty(database.PetProfiles);
    }

    [Fact]
    public void DoneToDoneAndRepeatedPendingEdits_DoNotDuplicateRewardsOrReversals()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Done", Status = TaskItemStatus.Done });
        for (var index = 0; index < 4; index++) task = Change(tasks, user.Id, task, TaskItemStatus.Done);
        Assert.Equal(25, Pet(database, user.Id).TotalExperience);
        Assert.Equal(1, Pet(database, user.Id).CompletedTaskCount);
        task = Change(tasks, user.Id, task, TaskItemStatus.Todo);
        for (var index = 0; index < 4; index++) task = Change(tasks, user.Id, task, TaskItemStatus.Doing);
        Assert.Equal(0, Pet(database, user.Id).TotalExperience);
        Assert.Equal(0, Pet(database, user.Id).CompletedTaskCount);
        Assert.Equal(80, Pet(database, user.Id).Energy);
        Assert.Single(database.CompletionRewards);
    }

    [Fact]
    public void RepeatedDoneReopenCycles_ReuseReceiptAndCannotFarmXpEnergyOrStreak()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Toggle" });
        for (var index = 0; index < 10; index++)
        {
            task = Change(tasks, user.Id, task, TaskItemStatus.Done);
            Assert.Equal(25, Pet(database, user.Id).TotalExperience);
            Assert.Equal(92, Pet(database, user.Id).Energy);
            Assert.Equal(1, Pet(database, user.Id).StreakDays);
            task = Change(tasks, user.Id, task, TaskItemStatus.Todo);
            Assert.Equal(0, Pet(database, user.Id).TotalExperience);
            Assert.Equal(80, Pet(database, user.Id).Energy);
            Assert.Equal(0, Pet(database, user.Id).StreakDays);
        }
        Assert.Single(database.CompletionRewards);
    }

    [Theory]
    [InlineData(4, 2, 1, 75)]
    [InlineData(10, 3, 2, 125)]
    public void ReopenAcrossLevelThreshold_RecalculatesLevelAndRemainder(int completions, int beforeLevel, int afterLevel, int remaining)
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        TaskResponse? latest = null;
        for (var index = 0; index < completions; index++)
            latest = tasks.Create(user.Id, new CreateTaskRequest { Title = $"Done {index}", Status = TaskItemStatus.Done });
        Assert.Equal(beforeLevel, Pet(database, user.Id).Level);
        Assert.Equal(0, Pet(database, user.Id).Experience);

        Change(tasks, user.Id, latest!, TaskItemStatus.Doing);

        var pet = Pet(database, user.Id);
        Assert.Equal(afterLevel, pet.Level);
        Assert.Equal(remaining, pet.Experience);
        Assert.Equal((completions - 1) * 25, pet.TotalExperience);
        Assert.Equal(completions - 1, pet.CompletedTaskCount);
    }

    [Fact]
    public void SharedReversal_DebitsOriginalReceiverAndRecompletionCreditsNewActor()
    {
        using var database = Database();
        var owner = User(database);
        var member = User(database, "member");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Shared" });
        teams.Join(member.Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        tasks.Create(owner.Id, new CreateTaskRequest { Title = "Owner credit", Status = TaskItemStatus.Done });
        var task = tasks.Create(member.Id, new CreateTaskRequest { Title = "Member credit", Status = TaskItemStatus.Done }, team.Team.Id);

        task = Change(tasks, owner.Id, task, TaskItemStatus.Todo, team.Team.Id);
        Assert.Equal(25, Pet(database, owner.Id).TotalExperience);
        Assert.Equal(0, Pet(database, member.Id).TotalExperience);
        task = Change(tasks, owner.Id, task, TaskItemStatus.Done, team.Team.Id);
        Assert.Equal(50, Pet(database, owner.Id).TotalExperience);
        Assert.Equal(0, Pet(database, member.Id).TotalExperience);
        Assert.Equal(owner.Id, database.CompletionRewards.Single(reward => reward.TaskId == task.Id).UserProfileId);
    }

    [Fact]
    public void SharedReversal_AfterReceiverLeftTeamStillDebitsThatReceiver()
    {
        using var database = Database();
        var owner = User(database);
        var member = User(database, "member");
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Shared" });
        teams.Join(member.Id, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        var task = tasks.Create(member.Id, new CreateTaskRequest { Title = "Member credit", Status = TaskItemStatus.Done }, team.Team.Id);
        teams.Leave(member.Id, team.Team.Id);

        Change(tasks, owner.Id, task, TaskItemStatus.Doing, team.Team.Id);

        Assert.Equal(0, Pet(database, member.Id).TotalExperience);
        Assert.DoesNotContain(database.PetProfiles, pet => pet.UserProfileId == owner.Id);
        Assert.NotNull(database.CompletionRewards.Single().RevokedAt);
    }

    [Fact]
    public void SharedReversal_AfterReceiverDeletedNeverRecreatesAccountOrPet()
    {
        using var database = Database();
        var owner = User(database);
        var member = User(database, "member");
        var memberId = member.Id;
        var teams = new TeamService(database);
        var team = teams.Create(owner.Id, new CreateTeamRequest { Name = "Shared" });
        teams.Join(memberId, new JoinTeamRequest { InviteCode = team.InviteCode });
        var tasks = Tasks(database);
        var task = tasks.Create(memberId, new CreateTaskRequest { Title = "Member credit", Status = TaskItemStatus.Done }, team.Team.Id);
        Users(database).DeleteProfile(memberId);

        task = Change(tasks, owner.Id, task, TaskItemStatus.Todo, team.Team.Id);

        Assert.False(database.UserProfiles.Any(user => user.Id == memberId));
        Assert.Empty(database.PetProfiles);
        Assert.Null(Assert.Single(database.CompletionRewards).UserProfileId);
        Assert.NotNull(database.CompletionRewards.Single().RevokedAt);
        Change(tasks, owner.Id, task, TaskItemStatus.Done, team.Team.Id);
        Assert.Equal(25, Pet(database, owner.Id).TotalExperience);
        Assert.Equal(owner.Id, database.CompletionRewards.Single().UserProfileId);
    }

    [Fact]
    public void Reversal_WhenReceiverPetIsMissingDoesNotRecreateIt()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Done", Status = TaskItemStatus.Done });
        database.PetProfiles.Remove(Pet(database, user.Id));
        database.SaveChanges();

        Change(tasks, user.Id, task, TaskItemStatus.Todo);

        Assert.Empty(database.PetProfiles);
        Assert.NotNull(database.CompletionRewards.Single().RevokedAt);
    }

    [Fact]
    public void UnknownLegacyReceiver_IsNotGuessedFromCreatorOrReopeningActor()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        tasks.Create(user.Id, new CreateTaskRequest { Title = "Known credit", Status = TaskItemStatus.Done });
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Legacy imported" });
        var stored = database.Tasks.Single(item => item.Id == task.Id);
        stored.Status = TaskItemStatus.Done;
        stored.IsCompleted = true;
        stored.CompletionRewardedAt = DateTime.UtcNow.AddDays(-1);
        database.CompletionRewards.Add(new CompletionReward
        { Task = stored, UserProfileId = null, AwardedAt = stored.CompletionRewardedAt.Value, Experience = 25 });
        database.SaveChanges();

        task = Change(tasks, user.Id, task, TaskItemStatus.Todo);

        Assert.Equal(25, Pet(database, user.Id).TotalExperience);
        Assert.NotNull(database.CompletionRewards.Single(reward => reward.TaskId == task.Id).RevokedAt);
        Change(tasks, user.Id, task, TaskItemStatus.Done);
        Assert.Equal(50, Pet(database, user.Id).TotalExperience);
    }

    [Fact]
    public void DeleteCompletedTask_RetainsXpAndReceiptForActivityRecalculation()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var deleted = tasks.Create(user.Id, new CreateTaskRequest { Title = "Keep its XP", Status = TaskItemStatus.Done });
        Assert.True(tasks.Delete(user.Id, deleted.Id, deleted.Version));
        Assert.Null(Assert.Single(database.CompletionRewards).TaskId);
        Assert.Null(database.CompletionRewards.Single().RevokedAt);
        Assert.Equal(25, Pet(database, user.Id).TotalExperience);
        var other = tasks.Create(user.Id, new CreateTaskRequest { Title = "Temporary XP", Status = TaskItemStatus.Done });
        Change(tasks, user.Id, other, TaskItemStatus.Todo);

        Assert.Equal(25, Pet(database, user.Id).TotalExperience);
        Assert.Equal(1, Pet(database, user.Id).CompletedTaskCount);
        Assert.Equal(1, Pet(database, user.Id).StreakDays);
        Assert.NotNull(Pet(database, user.Id).LastCompletedAt);
    }

    [Fact]
    public void DeleteTeam_WithUntrackedReceiptsRetainsEarnedXpAndHistory()
    {
        var options = DatabaseOptions();
        int userId;
        int teamId;
        using (var setup = new AppDbContext(options))
        {
            userId = User(setup).Id;
            teamId = new TeamService(setup).Create(userId, new CreateTeamRequest { Name = "Disposable" }).Team.Id;
            Tasks(setup).Create(userId, new CreateTaskRequest { Title = "Keep reward", Status = TaskItemStatus.Done }, teamId);
        }
        using (var deletion = new AppDbContext(options))
            new TeamService(deletion).Delete(userId, teamId);
        using var check = new AppDbContext(options);

        Assert.Empty(check.Tasks);
        Assert.Empty(check.Teams);
        Assert.Null(Assert.Single(check.CompletionRewards).TaskId);
        Assert.Null(check.CompletionRewards.Single().RevokedAt);
        Assert.Equal(25, Pet(check, userId).TotalExperience);
    }

    [Fact]
    public void ReversingMiddleOrLastCompletion_RebuildsStreakAndLastCompletedAt()
    {
        using var database = Database();
        var user = User(database);
        var tasks = Tasks(database);
        var pets = Pets(database);
        var today = DateTime.UtcNow.Date;
        var items = new List<TaskResponse>();
        foreach (var offset in new[] { -2, -1, 0 })
        {
            var task = tasks.Create(user.Id, new CreateTaskRequest { Title = $"Day {offset}" });
            var stored = database.Tasks.Single(item => item.Id == task.Id);
            stored.Status = TaskItemStatus.Done;
            stored.IsCompleted = true;
            pets.ApplyTaskCompletionReward(stored, user.Id, today.AddDays(offset));
            database.SaveChanges();
            items.Add(tasks.GetById(user.Id, task.Id)!);
        }
        Assert.Equal(3, Pet(database, user.Id).StreakDays);

        Change(tasks, user.Id, items[1], TaskItemStatus.Todo);
        Assert.Equal(1, Pet(database, user.Id).StreakDays);
        Assert.Equal(today, Pet(database, user.Id).LastCompletedAt);
        Change(tasks, user.Id, items[2], TaskItemStatus.Todo);
        Assert.Equal(1, Pet(database, user.Id).StreakDays);
        Assert.Equal(today.AddDays(-2), Pet(database, user.Id).LastCompletedAt);
        Change(tasks, user.Id, items[0], TaskItemStatus.Todo);
        Assert.Equal(0, Pet(database, user.Id).StreakDays);
        Assert.Null(Pet(database, user.Id).LastCompletedAt);
    }

    [Fact]
    public void Reversal_PreservesUnknownLegacyActivityAndActualLevelUpEnergyBaseline()
    {
        using var database = Database();
        var user = User(database);
        var baselineDate = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
        database.PetProfiles.Add(new PetProfile
        {
            UserProfileId = user.Id, TotalExperience = 75, Experience = 75, CompletedTaskCount = 3,
            Level = 1, Energy = 68, LastCompletedAt = baselineDate, StreakDays = 3,
            LegacyLastCompletedAt = baselineDate, LegacyStreakDays = 3
        });
        database.SaveChanges();
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Fourth day", Status = TaskItemStatus.Done });
        Assert.Equal(2, Pet(database, user.Id).Level);
        Assert.Equal(100, Pet(database, user.Id).Energy);
        Assert.Equal(4, Pet(database, user.Id).StreakDays);

        Change(tasks, user.Id, task, TaskItemStatus.Todo);

        var pet = Pet(database, user.Id);
        Assert.Equal(75, pet.TotalExperience);
        Assert.Equal(3, pet.CompletedTaskCount);
        Assert.Equal(1, pet.Level);
        Assert.Equal(68, pet.Energy);
        Assert.Equal(3, pet.StreakDays);
        Assert.Equal(baselineDate, pet.LastCompletedAt);
    }

    [Fact]
    public void Reversal_UsesActualCappedEnergyGain()
    {
        using var database = Database();
        var user = User(database);
        database.PetProfiles.Add(new PetProfile { UserProfileId = user.Id, Energy = 95 });
        database.SaveChanges();
        var tasks = Tasks(database);
        var task = tasks.Create(user.Id, new CreateTaskRequest { Title = "Capped", Status = TaskItemStatus.Done });
        Assert.Equal(5, database.CompletionRewards.Single().EnergyGranted);

        Change(tasks, user.Id, task, TaskItemStatus.Todo);

        Assert.Equal(95, Pet(database, user.Id).Energy);
        Assert.Equal(0, Pet(database, user.Id).TotalExperience);
    }

    [Fact]
    public async Task ConcurrentDoneAndReopenRequests_ApplyAndRevokeOnlyOnce()
    {
        var options = DatabaseOptions();
        int userId;
        int taskId;
        using (var setup = new AppDbContext(options))
        {
            userId = User(setup).Id;
            taskId = Tasks(setup).Create(userId, new CreateTaskRequest { Title = "Concurrent" }).Id;
        }
        foreach (var status in new[] { TaskItemStatus.Done, TaskItemStatus.Todo })
        {
            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
            {
                using var database = new AppDbContext(options);
                Tasks(database).Update(userId, taskId, new UpdateTaskRequest { Title = "Concurrent", Status = status });
            })));
            using var check = new AppDbContext(options);
            Assert.Equal(status == TaskItemStatus.Done ? 25 : 0, Pet(check, userId).TotalExperience);
            Assert.Equal(status == TaskItemStatus.Done ? 1 : 0, Pet(check, userId).CompletedTaskCount);
            Assert.Single(check.CompletionRewards);
        }
    }
}
