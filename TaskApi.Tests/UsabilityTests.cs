using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class UsabilityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Assigned_completion_belongs_to_assignee_and_undo_keeps_original_receiver(bool createDone)
    {
        using var f = new Fixture();
        var task = f.Tasks.Create(f.Alice, new() { Title = "Assigned", AssigneeUserProfileId = f.Bob,
            Status = createDone ? TaskItemStatus.Done : TaskItemStatus.Todo }, f.Team);
        if (!createDone) task = f.Move(task, TaskItemStatus.Done);
        Assert.Equal(0, f.Pets.GetProfile(f.Alice).TotalExperience);
        Assert.Equal(25, f.Pets.GetProfile(f.Bob).TotalExperience);
        // Changing the assignee of an already completed task cannot transfer its receipt.
        task = f.Tasks.Update(f.Alice, task.Id, new() { Title = task.Title, Version = task.Version,
            Status = TaskItemStatus.Done, AssigneeUserProfileId = f.Alice }, f.Team)!;
        Assert.Equal(25, f.Pets.GetProfile(f.Bob).TotalExperience);
        var reopened = f.Move(task, TaskItemStatus.Doing);
        Assert.Equal(0, f.Pets.GetProfile(f.Bob).TotalExperience);
        f.Tasks.Undo(f.Alice, reopened.Undo!.Token, f.Team);
        Assert.Equal(25, f.Pets.GetProfile(f.Bob).TotalExperience);
        Assert.Equal(0, f.Pets.GetProfile(f.Alice).TotalExperience);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("work")]
    public void Status_counts_cover_all_filtered_pages_without_leaking_other_scopes(string? tag)
    {
        using var f = new Fixture();
        var doing = f.Tasks.Create(f.Alice, new() { Title = "Shared work", Status = TaskItemStatus.Doing, Tags = "work" }, f.Team);
        f.Tasks.Create(f.Alice, new() { Title = "Shared work", Status = TaskItemStatus.Done, Tags = "work" }, f.Team);
        f.Tasks.Create(f.Alice, new() { Title = "Private work", Status = TaskItemStatus.Doing, Tags = "work" });
        var page = f.Tasks.GetAll(f.Bob, null, null, null, null, "desc", "Shared", 1, 1, f.Team, tag);
        Assert.Single(page.Items);
        Assert.Equal(TaskItemStatus.Done, page.Items[0].Status);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new TaskStatusCounts(0, 1, 1), page.StatusCounts);
        var filtered = f.Tasks.GetAll(f.Bob, null, TaskItemStatus.Doing, null, null, "desc", "Shared", 1, 1, f.Team, tag);
        Assert.Equal(doing.Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(new TaskStatusCounts(0, 1, 0), filtered.StatusCounts);
    }

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public UserProfileService Users { get; }
        public TaskService Tasks { get; }
        public PetService Pets { get; }
        public TeamService Teams { get; }
        public int Alice { get; }
        public int Bob { get; }
        public int Outside { get; }
        public int Team { get; }
        public Fixture()
        {
            Users = new(Db, NullLogger<UserProfileService>.Instance);
            Pets = new(Db, NullLogger<PetService>.Instance, Users);
            Tasks = new(Db, NullLogger<TaskService>.Instance, Pets, Users);
            Teams = new(Db);
            Alice = Users.GetOrCreateByKey("alice").Id; Bob = Users.GetOrCreateByKey("bob").Id; Outside = Users.GetOrCreateByKey("outside").Id;
            var created = Teams.Create(Alice, new() { Name = "Shared" }); Team = created.Team.Id;
            Teams.Join(Bob, new() { InviteCode = created.InviteCode });
        }
        public TaskResponse Create(TaskItemStatus status = TaskItemStatus.Todo, int? teamId = null) => Tasks.Create(Alice, new() { Title = "Task", Status = status }, teamId);
        public TaskResponse Move(TaskResponse task, TaskItemStatus status, int? actor = null) => Tasks.Update(actor ?? Alice, task.Id, new()
        { Title = task.Title, Description = task.Description, Status = status, Version = task.Version,
            AssigneeUserProfileId = task.AssigneeUserProfileId, Checklist = task.Checklist, Tags = task.Tags }, task.TeamId)!;
        public void Dispose() => Db.Dispose();
    }

    [Fact] public void Assignee_and_checklist_roundtrip_without_reward()
    {
        using var f = new Fixture();
        var task = f.Tasks.Create(f.Alice, new() { Title = "Shared", AssigneeUserProfileId = f.Bob,
            Checklist = [new() { Text = " UI ", IsCompleted = true }, new() { Text = "Test" }] }, f.Team);
        Assert.Equal(f.Bob, task.AssigneeUserProfileId); Assert.Equal("bob", task.AssigneeDisplayName);
        Assert.Equal("UI", task.Checklist[0].Text); Assert.False(task.IsCompleted); Assert.Empty(f.Db.CompletionRewards);
        var fetched = f.Tasks.GetById(f.Bob, task.Id, f.Team)!;
        Assert.Equal(2, fetched.Checklist.Count);
    }

    [Fact] public void Outsiders_and_personal_assignments_rejected()
    {
        using var f = new Fixture();
        Assert.Throws<TeamOperationException>(() => f.Tasks.Create(f.Alice, new() { Title = "No", AssigneeUserProfileId = f.Outside }, f.Team));
        Assert.Throws<TeamOperationException>(() => f.Tasks.Create(f.Alice, new() { Title = "No", AssigneeUserProfileId = f.Alice }));
        Assert.Throws<TeamOperationException>(() => f.Tasks.Create(f.Outside, new() { Title = "No" }, f.Team));
        Assert.Empty(f.Db.Tasks);
    }

    [Fact] public void Checklist_limits_and_atomic_validation()
    {
        using var f = new Fixture();
        foreach (var list in new List<ChecklistItem>[] { [new() { Text = " " }], [new() { Text = new string('x', 121) }], [new() { Text = "line\nbreak" }], Enumerable.Range(0, 21).Select(i => new ChecklistItem { Text = "item" }).ToList() })
            Assert.Throws<TeamOperationException>(() => f.Tasks.Create(f.Alice, new() { Title = "No", Checklist = list }));
        var task = f.Create(teamId: f.Team);
        Assert.Throws<TeamOperationException>(() => f.Tasks.Update(f.Alice, task.Id, new() { Title = "Changed", Version = task.Version,
            AssigneeUserProfileId = f.Bob, Checklist = [new() { Text = "" }] }, f.Team));
        Assert.Equal("Task", f.Db.Tasks.Single().Title); Assert.Null(f.Db.Tasks.Single().AssigneeUserProfileId);
    }

    [Fact] public void Leaving_unassigns_and_invalidates_undo()
    {
        using var f = new Fixture();
        var task = f.Tasks.Create(f.Alice, new() { Title = "Work", AssigneeUserProfileId = f.Bob }, f.Team);
        var moved = f.Move(task, TaskItemStatus.Doing, f.Bob);
        f.Teams.Leave(f.Bob, f.Team);
        Assert.Null(f.Tasks.GetById(f.Alice, task.Id, f.Team)!.AssigneeUserProfileId);
        Assert.Empty(f.Db.TaskUndoEntries);
        Assert.Throws<TeamOperationException>(() => f.Tasks.Undo(f.Bob, moved.Undo!.Token, f.Team));
    }

    [Fact] public void Account_removal_unassigns_without_deleting_shared_task()
    {
        using var f = new Fixture();
        var task = f.Tasks.Create(f.Alice, new() { Title = "Work", AssigneeUserProfileId = f.Bob }, f.Team);
        f.Users.DeleteProfile(f.Bob);
        Assert.Null(f.Tasks.GetById(f.Alice, task.Id, f.Team)!.AssigneeUserProfileId);
    }

    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Doing)]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Doing, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Todo)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Doing)]
    public void Move_undo_restores_status_and_net_XP(TaskItemStatus before, TaskItemStatus after)
    {
        using var f = new Fixture(); var task = f.Create(before);
        var xp = f.Db.PetProfiles.Select(p => p.TotalExperience).SingleOrDefault();
        var moved = f.Move(task, after); Assert.NotNull(moved.Undo);
        var restored = f.Tasks.Undo(f.Alice, moved.Undo.Token);
        Assert.Equal(before, restored.Status); Assert.Equal(xp, f.Db.PetProfiles.Select(p => p.TotalExperience).SingleOrDefault());
        Assert.Empty(f.Db.TaskUndoEntries);
        Assert.Throws<TeamOperationException>(() => f.Tasks.Undo(f.Alice, moved.Undo.Token));
    }

    [Fact] public void Reopening_and_undoing_restores_original_receiver_and_date()
    {
        using var f = new Fixture(); var task = f.Create(TaskItemStatus.Done, f.Team);
        var date = f.Db.CompletionRewards.Single().AwardedAt;
        var moved = f.Move(task, TaskItemStatus.Todo, f.Bob);
        Assert.Equal(0, f.Db.PetProfiles.Single().TotalExperience);
        f.Tasks.Undo(f.Bob, moved.Undo!.Token, f.Team);
        Assert.Equal(25, f.Db.PetProfiles.Single().TotalExperience);
        Assert.Equal(f.Alice, f.Db.PetProfiles.Single().UserProfileId);
        Assert.Equal(date, f.Db.CompletionRewards.Single().AwardedAt);
        Assert.Equal(f.Alice, f.Db.CompletionRewards.Single().UserProfileId);
    }

    [Fact] public void Undo_does_not_recreate_deleted_reward_receiver()
    {
        using var f = new Fixture();
        var task = f.Tasks.Create(f.Bob, new() { Title = "Done", Status = TaskItemStatus.Done }, f.Team);
        var moved = f.Move(task, TaskItemStatus.Todo);
        f.Users.DeleteProfile(f.Bob);
        // Deleting the creator invalidates a snapshot's version when needed; use
        // the revoked receipt path directly to prove it never creates a user.
        f.Pets.RestoreRevokedTaskCompletionReward(f.Db.Tasks.Single(), task.CreatedAt, DateTime.UtcNow);
        f.Db.SaveChanges();
        Assert.DoesNotContain(f.Db.UserProfiles, user => user.Id == f.Bob);
        Assert.Empty(f.Db.PetProfiles);
    }

    [Fact] public void Undo_is_actor_scope_expiry_and_version_bound()
    {
        using var f = new Fixture(); var task = f.Create(teamId: f.Team); var moved = f.Move(task, TaskItemStatus.Doing);
        Assert.Throws<TeamOperationException>(() => f.Tasks.Undo(f.Bob, moved.Undo!.Token, f.Team));
        Assert.Throws<TeamOperationException>(() => f.Tasks.Undo(f.Alice, moved.Undo!.Token));
        f.Tasks.Update(f.Bob, task.Id, new() { Title = "Other edit", Status = TaskItemStatus.Doing, Version = moved.Version }, f.Team);
        Assert.Throws<TeamOperationException>(() => f.Tasks.Undo(f.Alice, moved.Undo!.Token, f.Team));
        var current = f.Tasks.GetById(f.Alice, task.Id, f.Team)!;
        var next = f.Move(current, TaskItemStatus.Todo);
        f.Db.TaskUndoEntries.Single(item => item.Id == next.Undo!.Token).ExpiresAt = DateTime.UtcNow.AddSeconds(-1); f.Db.SaveChanges();
        Assert.Throws<TeamOperationException>(() => f.Tasks.Undo(f.Alice, next.Undo!.Token, f.Team));
    }

    [Fact] public void Delete_undo_keeps_identity_and_reward_receipt_without_duplicate_XP()
    {
        using var f = new Fixture(); var task = f.Create(TaskItemStatus.Done, f.Team);
        var receiptId = f.Db.CompletionRewards.Single().Id;
        var undo = f.Tasks.DeleteWithUndo(f.Bob, task.Id, task.Version, f.Team)!;
        Assert.Empty(f.Db.Tasks); Assert.Null(f.Db.CompletionRewards.Single().TaskId);
        var restored = f.Tasks.Undo(f.Bob, undo.Token, f.Team);
        Assert.Equal(task.Id, restored.Id); Assert.Equal(task.CreatedAt, restored.CreatedAt);
        Assert.Equal(task.Id, f.Db.CompletionRewards.Single().TaskId); Assert.Equal(receiptId, f.Db.CompletionRewards.Single().Id);
        Assert.Equal(25, f.Db.PetProfiles.Single().TotalExperience);
        f.Move(restored, TaskItemStatus.Todo, f.Bob); Assert.Equal(0, f.Db.PetProfiles.Single().TotalExperience);
    }

    [Fact] public void Tag_rename_merge_and_delete_update_only_matching_scope_and_tokens()
    {
        using var f = new Fixture(); var tags = new TaskTagService(f.Db);
        var personal = f.Tasks.Create(f.Alice, new() { Title = "Private", Tags = "art, artist" });
        var shared = f.Tasks.Create(f.Alice, new() { Title = "Shared", Tags = "art, artist, review" }, f.Team);
        tags.Create(f.Alice, new() { Name = "art" }, f.Team); tags.Create(f.Alice, new() { Name = "review" }, f.Team);
        tags.Manage(f.Alice, new() { Name = "ART", Action = "rename", TargetName = "design" }, f.Team);
        Assert.Equal("design, artist, review", f.Tasks.GetById(f.Alice, shared.Id, f.Team)!.Tags);
        Assert.Equal("art, artist", f.Tasks.GetById(f.Alice, personal.Id)!.Tags);
        tags.Manage(f.Bob, new() { Name = "design", Action = "merge", TargetName = "review" }, f.Team);
        Assert.Equal("review, artist", f.Tasks.GetById(f.Alice, shared.Id, f.Team)!.Tags);
        tags.Manage(f.Alice, new() { Name = "review", Action = "delete" }, f.Team);
        Assert.Equal("artist", f.Tasks.GetById(f.Alice, shared.Id, f.Team)!.Tags); Assert.Equal(2, f.Db.Tasks.Count());
        Assert.Empty(f.Db.CompletionRewards);
    }

    [Fact] public void Tag_invalid_merge_collision_missing_source_and_overflow_are_atomic()
    {
        using var f = new Fixture(); var tags = new TaskTagService(f.Db);
        f.Tasks.Create(f.Alice, new() { Title = "Long", Tags = "a," + new string('x', 296) });
        tags.Create(f.Alice, new() { Name = "a" }); tags.Create(f.Alice, new() { Name = "b" });
        foreach (var request in new ManageTaskTagRequest[] {
            new() { Name = "a", Action = "rename", TargetName = "b" }, new() { Name = "a", Action = "merge", TargetName = "missing" },
            new() { Name = "missing", Action = "delete" }, new() { Name = "a", Action = "rename", TargetName = "too-long" } })
            Assert.Throws<TeamOperationException>(() => tags.Manage(f.Alice, request));
        Assert.StartsWith("a,", f.Db.Tasks.Single().Tags); Assert.Contains(tags.GetAll(f.Alice).Items, item => item.Name == "a");
    }

    [Fact] public void Weekly_JST_Monday_boundaries_reversals_deleted_receipts_and_user_separation()
    {
        using var f = new Fixture();
        var now = new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Utc); // Tue 01:00 JST
        foreach (var date in new[] { "2026-09-06T14:59:59Z", "2026-09-06T15:00:00Z", "2026-09-07T15:00:00Z" })
            f.Db.CompletionRewards.Add(new() { UserProfileId = f.Alice, AwardedAt = DateTime.Parse(date).ToUniversalTime() });
        f.Db.CompletionRewards.Add(new() { UserProfileId = f.Alice, AwardedAt = now.AddMinutes(-5), RevokedAt = now });
        f.Db.CompletionRewards.Add(new() { UserProfileId = f.Bob, AwardedAt = now });
        f.Db.SaveChanges();
        var report = new WeeklyReviewService(f.Db).Get(f.Alice, now);
        Assert.Equal("2026-09-07", report.WeekStart); Assert.Equal("2026-09-13", report.WeekEnd);
        Assert.Equal(2, report.ThisWeek); Assert.Equal(1, report.LastWeek); Assert.Equal(1, report.Difference);
        Assert.Equal(1, report.Days[0].Completed); Assert.Equal(1, report.Days[1].Completed); Assert.Equal(7, report.Days.Count);
        Assert.Empty(f.Db.PetProfiles);
    }
}
