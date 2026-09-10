using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class WorkflowTests
{
    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public TaskService Tasks { get; }
        public TeamService Teams { get; }
        public int Alice { get; }
        public int Bob { get; }
        public int Outside { get; }
        public int Team { get; }
        public DateTime Today { get; } = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9).Date, DateTimeKind.Utc);
        public Fixture()
        {
            var users = new UserProfileService(Db, NullLogger<UserProfileService>.Instance);
            Tasks = new(Db, NullLogger<TaskService>.Instance, new PetService(Db, NullLogger<PetService>.Instance, users), users);
            Teams = new(Db); Alice = users.GetOrCreateByKey("alice").Id; Bob = users.GetOrCreateByKey("bob").Id; Outside = users.GetOrCreateByKey("outsider").Id;
            var created = Teams.Create(Alice, new() { Name = "Main team" }); Team = created.Team.Id;
            Teams.Join(Bob, new() { InviteCode = created.InviteCode });
        }
        public TaskResponse Create(string title, DateTime? due = null, TaskItemStatus status = TaskItemStatus.Todo, int? assignee = null, int? team = null) =>
            Tasks.Create(Alice, new() { Title = title, Description = "searchable description", DueDate = due, Status = status,
                AssigneeUserProfileId = assignee, Tags = "work" }, team ?? Team);
        public PagedResponse<TaskResponse> Filter(string? assignee = null, string? due = null, string sort = "desc", int page = 1, int size = 100, string? exactTag = null) =>
            Tasks.GetAll(Alice, null, null, null, null, sort, "searchable", page, size, Team, exactTag, false, assignee, due);
        public void Work(TaskResponse task, CompanionWorkState work) { Db.Tasks.Single(row => row.Id == task.Id).CompanionJson = JsonSerializer.Serialize(work); Db.SaveChanges(); }
        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public void Due_filters_use_JST_dates_and_exclude_completed_and_undated_tasks()
    {
        using var f = new Fixture();
        var late = f.Create("Late", f.Today.AddDays(-1)); var today = f.Create("Today", f.Today);
        f.Create("Tomorrow", f.Today.AddDays(1)); var undated = f.Create("Undated"); f.Create("Done", f.Today.AddDays(-1), TaskItemStatus.Done);
        Assert.Equal(late.Id, Assert.Single(f.Filter(due: "overdue").Items).Id);
        Assert.Equal(today.Id, Assert.Single(f.Filter(due: "today").Items).Id);
        Assert.Equal(2, f.Filter(due: "through_today").TotalCount);
        Assert.Equal(undated.Id, Assert.Single(f.Filter(due: "none").Items).Id);
        Assert.Equal(5, f.Filter().TotalCount); // Description search spans every status.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("work")]
    public void Assignment_due_sort_and_status_counts_apply_before_paging(string? exactTag)
    {
        using var f = new Fixture();
        var late = f.Create("Late", f.Today.AddDays(-1), assignee: f.Alice);
        var next = f.Create("Next", f.Today, TaskItemStatus.Doing, f.Alice);
        var undated = f.Create("No date", assignee: f.Alice);
        f.Create("Bob", f.Today.AddDays(-2), assignee: f.Bob);
        f.Create("Unassigned", f.Today.AddDays(-3));
        var page = f.Filter("me", sort: "due", size: 1, exactTag: exactTag);
        Assert.Equal(late.Id, Assert.Single(page.Items).Id); Assert.Equal(3, page.TotalCount);
        Assert.Equal(new TaskStatusCounts(2, 1, 0), page.StatusCounts);
        Assert.Equal(next.Id, Assert.Single(f.Filter("me", sort: "due", page: 2, size: 1, exactTag: exactTag).Items).Id);
        Assert.Equal(undated.Id, Assert.Single(f.Filter("me", sort: "due", page: 3, size: 1, exactTag: exactTag).Items).Id);
        Assert.Single(f.Filter(f.Bob.ToString()).Items); Assert.Single(f.Filter("unassigned").Items);
    }

    [Fact]
    public void Invalid_filters_and_nonmember_scopes_are_rejected()
    {
        using var f = new Fixture();
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => f.Filter(f.Outside.ToString())).StatusCode);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => f.Filter(due: "yesterdayish")).StatusCode);
        Assert.Equal(404, Assert.Throws<TeamOperationException>(() => f.Tasks.GetAll(f.Outside, null, null, null, null, "due", null, 1, 10, f.Team, assignee: "me")).StatusCode);
    }

    [Fact]
    public void Inbox_includes_all_joined_teams_but_never_private_notes_other_recipients_or_nonmember_teams()
    {
        using var f = new Fixture();
        var directed = f.Create("Directed"); f.Work(directed, new() {
            Help = new() { AuthorId = f.Bob, RecipientId = f.Alice, Message = "Please review" },
            Savepoints = [new() { UserId = f.Bob, NextStep = "PRIVATE-SAVEPOINT-SECRET" }] });
        var broadcast = f.Create("Team request"); f.Work(broadcast, new() { Help = new() { AuthorId = f.Bob, Message = "Anyone?" } });
        var own = f.Create("Outgoing"); f.Work(own, new() { Help = new() { AuthorId = f.Alice, RecipientId = f.Bob, Message = "For Bob" } });
        var other = f.Teams.Create(f.Alice, new() { Name = "Second team" });
        f.Teams.Join(f.Bob, new() { InviteCode = other.InviteCode });
        var handoff = f.Create("Handoff", team: other.Team.Id); f.Work(handoff, new() { Handoff = new() { FromUserId = f.Bob, ToUserId = f.Alice, Request = "Confirm this" } });
        var inbox = f.Tasks.GetWorkInbox(f.Alice);
        Assert.Equal(2, inbox.DirectCount); Assert.Equal(1, inbox.TeamCount); Assert.Equal(3, inbox.Items.Count);
        Assert.Equal(2, inbox.Items.Select(item => item.TeamId).Distinct().Count());
        Assert.DoesNotContain("PRIVATE-SAVEPOINT-SECRET", JsonSerializer.Serialize(inbox));
        Assert.DoesNotContain(inbox.Items, item => item.TaskId == own.Id);
        Assert.Empty(f.Tasks.GetWorkInbox(f.Outside).Items);
        Assert.DoesNotContain(f.Tasks.GetWorkInbox(f.Bob).Items, item => item.TaskId == directed.Id);
    }

    [Fact]
    public void Inbox_updates_after_offer_withdraw_resolve_and_membership_loss()
    {
        using var f = new Fixture(); var task = f.Create("Please help");
        var initial = f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team);
        var opened = f.Tasks.ChangeCompanionWork(f.Alice, task.Id, new() { Action = "help_open", Version = initial.Version,
            ExpectedUpdatedAt = initial.UpdatedAt, RecipientId = f.Bob, Kind = "review", Message = "Help" }, f.Team);
        Assert.Single(f.Tasks.GetWorkInbox(f.Bob).Items);
        var offer = f.Tasks.ChangeCompanionWork(f.Bob, task.Id, new() { Action = "help_offer", Version = opened.Version,
            ExpectedUpdatedAt = opened.UpdatedAt, EntryId = opened.Help!.Id }, f.Team);
        Assert.Empty(f.Tasks.GetWorkInbox(f.Bob).Items);
        var withdrawn = f.Tasks.ChangeCompanionWork(f.Bob, task.Id, new() { Action = "help_withdraw", Version = offer.Version,
            ExpectedUpdatedAt = offer.UpdatedAt, EntryId = offer.Help!.Id }, f.Team);
        Assert.Single(f.Tasks.GetWorkInbox(f.Bob).Items);
        f.Tasks.ChangeCompanionWork(f.Alice, task.Id, new() { Action = "help_resolve", Version = withdrawn.Version,
            ExpectedUpdatedAt = withdrawn.UpdatedAt, EntryId = withdrawn.Help!.Id }, f.Team);
        Assert.Empty(f.Tasks.GetWorkInbox(f.Bob).Items);
        f.Work(task, new() { Help = new() { AuthorId = f.Alice, Message = "Team request" } });
        Assert.Single(f.Tasks.GetWorkInbox(f.Bob).Items);
        f.Teams.Leave(f.Bob, f.Team); Assert.Empty(f.Tasks.GetWorkInbox(f.Bob).Items);
    }

    [Fact]
    public void Inbox_excludes_claimed_cancelled_and_unavailable_SOS_and_routes_handoff_questions_to_sender()
    {
        using var f = new Fixture();
        foreach (var help in new WorkHelp[] { new() { AuthorId = f.Bob, HelperId = f.Alice },
            new() { AuthorId = f.Bob, Status = "cancelled" }, new() { AuthorId = f.Bob, RecipientUnavailable = true } })
            f.Work(f.Create("Inactive"), new() { Help = help });
        var task = f.Create("Question"); f.Work(task, new() { Handoff = new() {
            FromUserId = f.Alice, ToUserId = f.Bob, Status = "question", Reply = "Please clarify" } });
        var item = Assert.Single(f.Tasks.GetWorkInbox(f.Alice).Items);
        Assert.Equal("Please clarify", item.Message); Assert.Equal("handoff", item.Kind);
        Assert.Empty(f.Tasks.GetWorkInbox(f.Bob).Items);
    }

    [Fact]
    public void Inbox_views_include_displayed_versions_people_and_only_the_viewers_actions()
    {
        using var f = new Fixture(); var task = f.Create("Review this");
        f.Work(task, new() { Help = new() { AuthorId = f.Alice, RecipientId = f.Bob, Kind = "review", Message = "Check the layout" } });
        var received = Assert.Single(f.Tasks.GetWorkInbox(f.Bob).Items);
        Assert.Equal(["help_offer"], received.Actions);
        Assert.Equal(f.Db.UserProfiles.Single(user => user.Id == f.Alice).DisplayName, received.AuthorName);
        Assert.Equal(f.Db.UserProfiles.Single(user => user.Id == f.Bob).DisplayName, received.RecipientName);
        Assert.Equal(f.Db.Tasks.Single(row => row.Id == task.Id).UpdatedAt, received.UpdatedAt);
        var sent = f.Tasks.GetWorkInbox(f.Alice, "sent");
        Assert.Equal(1, sent.SentCount); Assert.Equal(["help_resolve", "help_cancel"], Assert.Single(sent.Items).Actions);
        f.Tasks.ChangeCompanionWork(f.Bob, task.Id, new() { Action = "help_offer", EntryId = received.Id,
            Version = received.Version, ExpectedUpdatedAt = received.UpdatedAt }, f.Team);
        var helping = f.Tasks.GetWorkInbox(f.Bob, "helping");
        Assert.Equal(1, helping.HelpingCount); Assert.Equal(["help_withdraw"], Assert.Single(helping.Items).Actions);
        Assert.Equal("helping", Assert.Single(f.Tasks.GetWorkInbox(f.Alice, "sent").Items).Status);
        Assert.Empty(f.Tasks.GetWorkInbox(f.Bob).Items);
        foreach (var view in new[] { "incoming", "helping", "sent" }) Assert.Empty(f.Tasks.GetWorkInbox(f.Outside, view).Items);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => f.Tasks.GetWorkInbox(f.Alice, "unknown")).StatusCode);
    }

    [Fact]
    public void Inbox_action_rejects_a_version_changed_after_the_list_was_read()
    {
        using var f = new Fixture(); var task = f.Create("Review this");
        f.Work(task, new() { Help = new() { AuthorId = f.Alice, Message = "Original request" } });
        var item = Assert.Single(f.Tasks.GetWorkInbox(f.Bob).Items);
        f.Tasks.ChangeCompanionWork(f.Alice, task.Id, new() { Action = "savepoint_save", NextStep = "Private update",
            Version = item.Version, ExpectedUpdatedAt = item.UpdatedAt }, f.Team);
        Assert.Equal(409, Assert.Throws<TeamOperationException>(() => f.Tasks.ChangeCompanionWork(f.Bob, task.Id,
            new() { Action = "help_offer", EntryId = item.Id, Version = item.Version, ExpectedUpdatedAt = item.UpdatedAt }, f.Team)).StatusCode);
        Assert.Null(f.Tasks.GetCompanionWork(f.Bob, task.Id, f.Team).Help!.HelperId);
    }
}
