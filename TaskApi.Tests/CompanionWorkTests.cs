using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;
using TaskApi.Services;

namespace TaskApi.Tests;

public sealed class CompanionWorkTests
{
    [Fact]
    public void Directed_SOS_is_shared_but_only_recipient_can_offer_without_changing_assignment_or_XP()
    {
        using var f = new Fixture(); var task = f.Create();
        var opened = f.Change(task, "help_open", setup: r => { r.Kind = "review"; r.Message = "Please review"; r.RecipientId = f.Bob; });
        var h = opened.Help!;
        Assert.Equal(f.Bob, h.RecipientId);
        Assert.Equal(f.Bob, f.Tasks.GetCompanionWork(f.Charlie, task.Id, f.Team).Help!.RecipientId);
        Assert.Equal(f.Bob, Assert.Single(f.Tasks.GetCompanionDashboard(f.Bob, f.Team).Help).Help!.RecipientId);
        var before = f.Db.Tasks.Single().CompanionJson;
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => f.Change(task, "help_offer", f.Charlie, r => r.EntryId = h.Id)).StatusCode);
        Assert.Equal(before, f.Db.Tasks.Single().CompanionJson);
        Assert.Equal(f.Bob, f.Change(task, "help_offer", f.Bob, r => r.EntryId = h.Id).Help!.HelperId);
        Assert.Null(f.Change(task, "help_withdraw", f.Bob, r => r.EntryId = h.Id).Help!.HelperId);
        Assert.Equal(f.Bob, f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Help!.RecipientId);
        f.Change(task, "help_offer", f.Bob, r => r.EntryId = h.Id);
        var resolved = f.Change(task, "help_resolve", setup: r => r.EntryId = h.Id);
        Assert.Equal(f.Bob, Assert.Single(resolved.Thanks).RecipientId);
        Assert.Null(f.Tasks.GetById(f.Alice, task.Id, f.Team)!.AssigneeUserProfileId);
        Assert.Equal(TaskItemStatus.Doing, f.Tasks.GetById(f.Alice, task.Id, f.Team)!.Status);
        Assert.Empty(f.Db.CompletionRewards);
    }

    [Fact]
    public void Directed_SOS_does_not_give_team_owner_the_recipients_response_permission()
    {
        using var f = new Fixture(); var task = f.Create();
        var h = f.Change(task, "help_open", f.Bob, r => { r.Kind = "review"; r.Message = "Please"; r.RecipientId = f.Charlie; }).Help!;
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() => f.Change(task, "help_offer", f.Alice, r => r.EntryId = h.Id)).StatusCode);
        f.Change(task, "help_cancel", f.Alice, r => r.EntryId = h.Id); // Existing moderation authority remains.
    }

    [Theory]
    [InlineData("self")]
    [InlineData("outsider")]
    [InlineData("left")]
    [InlineData("zero")]
    [InlineData("missing")]
    public void Invalid_SOS_recipient_is_rejected_without_partial_changes(string kind)
    {
        using var f = new Fixture(); var task = f.Create();
        if (kind == "left") f.Teams.Leave(f.Bob, f.Team);
        var id = kind switch { "self" => f.Alice, "outsider" => f.Outside, "left" => f.Bob, "zero" => 0, _ => int.MaxValue };
        var before = f.Db.Tasks.Single().CompanionJson;
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() => f.Change(task, "help_open",
            setup: r => { r.Kind = "review"; r.Message = "Please"; r.RecipientId = id; })).StatusCode);
        Assert.Equal(before, f.Db.Tasks.Single().CompanionJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recipient_removal_closes_SOS_in_live_tasks_and_Undo_snapshots(bool deleteAccount)
    {
        using var f = new Fixture(); var live = f.Create(); var deleted = f.Create();
        foreach (var task in new[] { live, deleted })
            f.Change(task, "help_open", setup: r => { r.Kind = "review"; r.Message = "Please"; r.RecipientId = f.Bob; });
        var current = f.Tasks.GetById(f.Alice, deleted.Id, f.Team)!;
        var undo = f.Tasks.DeleteWithUndo(f.Alice, current.Id, current.Version, f.Team)!;
        if (deleteAccount) f.Users.DeleteProfile(f.Bob); else f.Teams.Leave(f.Bob, f.Team);
        f.Tasks.Undo(f.Alice, undo.Token, f.Team);
        foreach (var task in new[] { live, deleted })
        {
            var h = f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Help!;
            Assert.Equal("cancelled", h.Status); Assert.True(h.RecipientUnavailable);
            Assert.Equal(deleteAccount ? (int?)null : f.Bob, h.RecipientId);
            Assert.Equal(409, Assert.Throws<TeamOperationException>(() => f.Change(task, "help_offer", f.Charlie, r => r.EntryId = h.Id)).StatusCode);
        }
        Assert.Empty(f.Tasks.GetCompanionDashboard(f.Alice, f.Team).Help);
    }

    [Fact]
    public void Legacy_SOS_keeps_all_team_recipient_and_deleted_recipient_is_anonymized_in_thanks()
    {
        var legacy = JsonSerializer.Deserialize<WorkHelp>("{}")!;
        Assert.Null(legacy.RecipientId); Assert.False(legacy.RecipientUnavailable);
        using var f = new Fixture(); var task = f.Create();
        var h = f.Change(task, "help_open", setup: r => { r.Kind = "review"; r.Message = "Please"; r.RecipientId = f.Bob; }).Help!;
        f.Change(task, "help_offer", f.Bob, r => r.EntryId = h.Id);
        f.Change(task, "help_resolve", setup: r => r.EntryId = h.Id);
        f.Users.DeleteProfile(f.Bob);
        var thanks = Assert.Single(f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Thanks);
        Assert.Null(thanks.RecipientId); Assert.Null(thanks.HelperId); Assert.True(thanks.RecipientUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Handoff_assigns_only_with_explicit_consent_on_recipient_acceptance(bool assign)
    {
        using var f = new Fixture();
        var task = f.Create();
        var sent = f.Change(task, "handoff_send", setup: r => {
            r.RecipientId = f.Bob; r.Message = "Please review"; r.Criteria = "Checked";
            r.AssignOnAccept = assign;
        });
        Assert.True(f.Tasks.GetById(f.Alice, task.Id, f.Team)!.HandoffPending);
        Assert.Null(f.Tasks.GetById(f.Alice, task.Id, f.Team)!.AssigneeUserProfileId);
        Assert.Equal(403, Assert.Throws<TeamOperationException>(() =>
            f.Change(task, "handoff_accept", setup: r => r.EntryId = sent.Handoff!.Id)).StatusCode);
        f.Change(task, "handoff_accept", f.Bob, r => r.EntryId = sent.Handoff!.Id);
        var accepted = f.Tasks.GetById(f.Alice, task.Id, f.Team)!;
        Assert.Equal(assign ? f.Bob : (int?)null, accepted.AssigneeUserProfileId);
        Assert.False(accepted.HandoffPending);
        Assert.Null(accepted.HandoffRecipientId);
        Assert.Equal(TaskItemStatus.Doing, accepted.Status);
        Assert.Empty(f.Db.CompletionRewards);
    }

    [Fact]
    public void One_field_work_memo_and_public_badges_do_not_leak_private_bookmarks()
    {
        using var f = new Fixture();
        var task = f.Create();
        f.Change(task, "savepoint_save", setup: r => r.NextStep = "private marker");
        f.Change(task, "note_add", setup: r => r.Learned = "Shared memo body");
        var help = f.Change(task, "help_open", setup: r => { r.Kind = "review"; r.Message = "Review please"; });
        var visible = f.Tasks.GetById(f.Bob, task.Id, f.Team)!;
        Assert.Equal(1, visible.NoteCount);
        Assert.True(visible.NeedsHelp);
        Assert.DoesNotContain("private marker", JsonSerializer.Serialize(visible));
        Assert.Null(f.Tasks.GetCompanionWork(f.Bob, task.Id, f.Team).Savepoint);
        Assert.Equal(400, Assert.Throws<TeamOperationException>(() =>
            f.Change(task, "note_add", setup: r => r.Tried = "Body required")).StatusCode);
        f.Change(task, "help_resolve", setup: r => r.EntryId = help.Help!.Id);
        Assert.False(f.Tasks.GetById(f.Bob, task.Id, f.Team)!.NeedsHelp);
    }

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public UserProfileService Users { get; }
        public TaskService Tasks { get; }
        public TeamService Teams { get; }
        public int Alice { get; }
        public int Bob { get; }
        public int Charlie { get; }
        public int Outside { get; }
        public int Team { get; }
        public Fixture()
        {
            Users = new(Db, NullLogger<UserProfileService>.Instance);
            Tasks = new(Db, NullLogger<TaskService>.Instance, new(Db, NullLogger<PetService>.Instance, Users), Users);
            Teams = new(Db);
            Alice = Users.GetOrCreateByKey("alice").Id; Bob = Users.GetOrCreateByKey("bob").Id;
            Charlie = Users.GetOrCreateByKey("charlie").Id; Outside = Users.GetOrCreateByKey("outside").Id;
            var created = Teams.Create(Alice, new() { Name = "Studio" }); Team = created.Team.Id;
            Teams.Join(Bob, new() { InviteCode = created.InviteCode }); Teams.Join(Charlie, new() { InviteCode = created.InviteCode });
        }
        public TaskResponse Create(bool shared = true, TaskItemStatus status = TaskItemStatus.Doing, string? tags = null) =>
            Tasks.Create(Alice, new() { Title = "Work", Status = status, Tags = tags }, shared ? Team : null);
        public CompanionWorkResponse Change(TaskResponse task, string action, int? actor = null, Action<CompanionWorkRequest>? setup = null)
        {
            var current = Tasks.GetCompanionWork(actor ?? Alice, task.Id, task.TeamId);
            var request = new CompanionWorkRequest { Action = action, Version = current.Version, ExpectedUpdatedAt = current.UpdatedAt };
            setup?.Invoke(request);
            return Tasks.ChangeCompanionWork(actor ?? Alice, task.Id, request, task.TeamId);
        }
        public void Dispose() => Db.Dispose();
    }

    [Fact] public void Savepoints_are_private_even_in_team_and_dashboard_and_do_not_reward()
    {
        using var f = new Fixture(); var task = f.Create();
        f.Change(task, "savepoint_save", setup: r => { r.NextStep = "Alice private"; r.Summary = "done so far"; });
        f.Change(task, "savepoint_save", f.Bob, r => r.NextStep = "Bob private");
        Assert.Equal("Alice private", f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Savepoint!.NextStep);
        var bob = JsonSerializer.Serialize(f.Tasks.GetCompanionDashboard(f.Bob, f.Team));
        Assert.DoesNotContain("Alice private", bob); Assert.Contains("Bob private", bob);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(f.Tasks.GetById(f.Bob, task.Id, f.Team)));
        Assert.Empty(f.Db.PetProfiles); Assert.Empty(f.Db.CompletionRewards);
        Assert.Equal(TaskItemStatus.Doing, f.Db.Tasks.Single().Status);
    }

    [Fact] public void Savepoint_can_be_replaced_cleared_and_is_hidden_for_done_without_losing_it()
    {
        using var f = new Fixture(); var task = f.Create(false);
        f.Change(task, "savepoint_save", setup: r => r.NextStep = "first");
        f.Change(task, "savepoint_save", setup: r => r.NextStep = "second");
        Assert.Single(f.Tasks.GetCompanionDashboard(f.Alice).Savepoints);
        var current = f.Tasks.GetById(f.Alice, task.Id)!;
        f.Tasks.Update(f.Alice, task.Id, new() { Title = task.Title, Status = TaskItemStatus.Done, Version = current.Version });
        Assert.Empty(f.Tasks.GetCompanionDashboard(f.Alice).Savepoints);
        Assert.Equal("second", f.Tasks.GetCompanionWork(f.Alice, task.Id).Savepoint!.NextStep);
        f.Change(task, "savepoint_clear"); Assert.Null(f.Tasks.GetCompanionWork(f.Alice, task.Id).Savepoint);
    }

    [Fact] public void Writes_require_both_revision_and_timestamp_and_reject_stale_context()
    {
        using var f = new Fixture(); var task = f.Create(); var before = f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team);
        var request = new CompanionWorkRequest { Action = "savepoint_save", NextStep = "later", Version = before.Version, ExpectedUpdatedAt = before.UpdatedAt };
        f.Change(task, "note_add", f.Bob, r => { r.Tried = "try"; r.Learned = "learn"; });
        Assert.Equal(409, Assert.Throws<TeamOperationException>(() => f.Tasks.ChangeCompanionWork(f.Alice, task.Id, request, f.Team)).StatusCode);
        request.ExpectedUpdatedAt = f.Db.Tasks.Single().UpdatedAt; request.Version = null;
        Assert.Throws<TeamOperationException>(() => f.Tasks.ChangeCompanionWork(f.Alice, task.Id, request, f.Team));
        Assert.Null(f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Savepoint);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test/\nsecret")]
    public void Unsafe_links_are_rejected_atomically(string url)
    {
        using var f = new Fixture(); var task = f.Create();
        Assert.Throws<TeamOperationException>(() => f.Change(task, "savepoint_save", setup: r => { r.NextStep = "next"; r.ResourceUrl = url; }));
        Assert.Equal("{}", f.Db.Tasks.Single().CompanionJson);
    }

    [Fact] public void Help_has_one_volunteer_and_requester_controls_resolution()
    {
        using var f = new Fixture(); var task = f.Create();
        var h = f.Change(task, "help_open", f.Bob, r => { r.Kind = "review"; r.Message = "look please"; }).Help!;
        Assert.Throws<TeamOperationException>(() => f.Change(task, "help_offer", f.Bob, r => r.EntryId = h.Id));
        f.Change(task, "help_offer", f.Charlie, r => r.EntryId = h.Id);
        Assert.Throws<TeamOperationException>(() => f.Change(task, "help_offer", setup: r => r.EntryId = h.Id));
        Assert.Throws<TeamOperationException>(() => f.Change(task, "help_resolve", f.Charlie, r => r.EntryId = h.Id));
        var result = f.Change(task, "help_resolve", f.Bob, r => r.EntryId = h.Id);
        Assert.Equal("resolved", result.Help!.Status); Assert.Equal(f.Charlie, result.Help.HelperId);
        Assert.Empty(f.Tasks.GetCompanionDashboard(f.Alice, f.Team).Help);
        Assert.Empty(f.Db.CompletionRewards); Assert.Null(f.Db.Tasks.Single().AssigneeUserProfileId);
        f.Change(task, "help_open", f.Bob, r => { r.Kind = "material"; r.Message = "next request"; });
        Assert.Single(f.Tasks.GetCompanionDashboard(f.Alice, f.Team).RecentThanks);
        Assert.Equal(h.Id, f.Tasks.GetCompanionWork(f.Charlie, task.Id, f.Team).Thanks.Single().Id);
    }

    [Fact] public void Help_withdraw_cancel_and_old_request_ID_are_guarded()
    {
        using var f = new Fixture(); var task = f.Create();
        var h = f.Change(task, "help_open", setup: r => { r.Kind = "decision"; r.Message = "decide"; }).Help!;
        f.Change(task, "help_offer", f.Bob, r => r.EntryId = h.Id);
        Assert.Throws<TeamOperationException>(() => f.Change(task, "help_withdraw", f.Charlie, r => r.EntryId = h.Id));
        Assert.Null(f.Change(task, "help_withdraw", f.Bob, r => r.EntryId = h.Id).Help!.HelperId);
        f.Change(task, "help_cancel", setup: r => r.EntryId = h.Id);
        f.Change(task, "help_open", setup: r => { r.Kind = "together"; r.Message = "think"; });
        Assert.Throws<TeamOperationException>(() => f.Change(task, "help_resolve", setup: r => r.EntryId = h.Id));
    }

    [Fact] public void Handoff_only_recipient_can_reply_and_question_can_be_resent()
    {
        using var f = new Fixture(); var task = f.Create();
        var h = f.Change(task, "handoff_send", setup: r => { r.RecipientId = f.Bob; r.Message = "three faces"; r.Criteria = "normal happy sad"; r.ResourceUrl = "https://example.test/spec"; }).Handoff!;
        Assert.Throws<TeamOperationException>(() => f.Change(task, "handoff_accept", f.Charlie, r => r.EntryId = h.Id));
        f.Change(task, "handoff_question", f.Bob, r => { r.EntryId = h.Id; r.Message = "what size?"; });
        var sent = f.Change(task, "handoff_send", setup: r => { r.RecipientId = f.Bob; r.Message = "32px faces"; r.Criteria = "three PNGs"; }).Handoff!;
        Assert.NotEqual(h.Id, sent.Id);
        var accepted = f.Change(task, "handoff_accept", f.Bob, r => r.EntryId = sent.Id);
        Assert.Equal("accepted", accepted.Handoff!.Status); Assert.Empty(f.Db.CompletionRewards);
        Assert.Null(f.Db.Tasks.Single().AssigneeUserProfileId); Assert.Equal(TaskItemStatus.Doing, f.Db.Tasks.Single().Status);
    }

    [Fact] public void Help_and_handoff_require_team_and_current_recipient()
    {
        using var f = new Fixture(); var personal = f.Create(false); var shared = f.Create();
        Assert.Throws<TeamOperationException>(() => f.Change(personal, "help_open", setup: r => { r.Kind = "review"; r.Message = "review"; }));
        foreach (var id in new[] { f.Alice, f.Outside, 999 })
            Assert.Throws<TeamOperationException>(() => f.Change(shared, "handoff_send", setup: r => { r.RecipientId = id; r.Message = "work"; r.Criteria = "done"; }));
    }

    [Fact] public void Notes_are_scoped_searchable_by_exact_tags_editable_and_deletable_by_author()
    {
        using var f = new Fixture(); var task = f.Create(tags: "art");
        var n = f.Change(task, "note_add", f.Bob, r => { r.Tried = "flood fill"; r.Learned = "preserve fur"; }).Notes.Single();
        Assert.Empty(f.Tasks.FindCompanionNotes(f.Alice, null, "artist", null, f.Team));
        Assert.Single(f.Tasks.FindCompanionNotes(f.Charlie, null, "ART", null, f.Team));
        Assert.Empty(f.Tasks.FindCompanionNotes(f.Alice, "fur", null, null));
        Assert.Empty(f.Tasks.FindCompanionNotes(f.Alice, "fur", null, task.Id, f.Team));
        Assert.Single(f.Tasks.FindCompanionNotes(f.Alice, "fur", null, null, f.Team));
        Assert.Throws<TeamOperationException>(() => f.Change(task, "note_delete", f.Charlie, r => r.EntryId = n.Id));
        f.Change(task, "note_edit", f.Bob, r => { r.EntryId = n.Id; r.Tried = "mask"; r.Learned = "works"; });
        Assert.Equal("works", f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Notes.Single().Learned);
        f.Change(task, "note_delete", f.Bob, r => r.EntryId = n.Id);
        Assert.Empty(f.Tasks.FindCompanionNotes(f.Alice, null, null, null, f.Team));
    }

    [Fact] public void Note_limits_do_not_modify_tracked_state_on_error()
    {
        using var f = new Fixture(); var task = f.Create();
        for (var i = 0; i < 12; i++) f.Change(task, "note_add", setup: r => { r.Tried = $"try{i}"; r.Learned = "learn"; });
        var before = f.Db.Tasks.Single().CompanionJson;
        Assert.Throws<TeamOperationException>(() => f.Change(task, "note_add", setup: r => { r.Tried = "extra"; r.Learned = "learn"; }));
        Assert.Equal(before, f.Db.Tasks.Single().CompanionJson);
        Assert.Throws<TeamOperationException>(() => f.Change(task, "note_edit", setup: r => { r.EntryId = f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Notes[0].Id; r.Tried = "changed"; r.Learned = ""; }));
        Assert.Equal(before, f.Db.Tasks.Single().CompanionJson);
    }

    [Fact] public void Showcase_requires_DONE_and_reopening_hides_without_losing_saved_artifact()
    {
        using var f = new Fixture(); var task = f.Create();
        void Fill(CompanionWorkRequest r) { r.Kind = "monitor"; r.Title = "Login"; r.Summary = "Login works"; r.ResourceUrl = "https://example.test/demo"; }
        Assert.Throws<TeamOperationException>(() => f.Change(task, "showcase_save", setup: Fill));
        f.Tasks.Update(f.Alice, task.Id, new() { Title = task.Title, Status = TaskItemStatus.Done, Version = task.Version }, f.Team);
        f.Change(task, "showcase_save", f.Bob, Fill);
        Assert.Single(f.Tasks.GetCompanionDashboard(f.Alice, f.Team).Showcase);
        Assert.Throws<TeamOperationException>(() => f.Change(task, "showcase_remove", f.Charlie));
        Assert.Equal(25, f.Db.PetProfiles.Single().TotalExperience);
        var current = f.Tasks.GetById(f.Alice, task.Id, f.Team)!;
        var moved = f.Tasks.Update(f.Alice, task.Id, new() { Title = task.Title, Status = TaskItemStatus.Doing, Version = current.Version }, f.Team)!;
        Assert.Empty(f.Tasks.GetCompanionDashboard(f.Alice, f.Team).Showcase);
        Assert.NotNull(f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Showcase);
        f.Tasks.Undo(f.Alice, moved.Undo!.Token, f.Team);
        Assert.Single(f.Tasks.GetCompanionDashboard(f.Alice, f.Team).Showcase);
        Assert.Equal(25, f.Db.PetProfiles.Single().TotalExperience);
    }

    [Fact] public void Deleted_task_Undo_preserves_all_companion_records_and_reward_once()
    {
        using var f = new Fixture(); var task = f.Create(status: TaskItemStatus.Done);
        f.Change(task, "savepoint_save", setup: r => r.NextStep = "next");
        f.Change(task, "note_add", setup: r => { r.Tried = "try"; r.Learned = "learn"; });
        f.Change(task, "showcase_save", setup: r => { r.Kind = "book"; r.Title = "Spec"; r.Summary = "ready"; });
        var before = f.Db.Tasks.Single().CompanionJson;
        var undo = f.Tasks.DeleteWithUndo(f.Bob, task.Id, f.Db.Tasks.Single().Version, f.Team)!;
        f.Tasks.Undo(f.Bob, undo.Token, f.Team);
        Assert.Equal(before, f.Db.Tasks.Single().CompanionJson); Assert.Equal(25, f.Db.PetProfiles.Single().TotalExperience);
    }

    [Fact] public void Leaving_removes_private_savepoints_even_from_Undo_and_cancels_outstanding_handoff()
    {
        using var f = new Fixture(); var task = f.Create();
        f.Change(task, "savepoint_save", f.Bob, r => r.NextStep = "private bookmark");
        f.Change(task, "handoff_send", setup: r => { r.RecipientId = f.Bob; r.Message = "work"; r.Criteria = "done"; });
        var undo = f.Tasks.DeleteWithUndo(f.Alice, task.Id, f.Db.Tasks.Single().Version, f.Team)!;
        f.Teams.Leave(f.Bob, f.Team);
        f.Tasks.Undo(f.Alice, undo.Token, f.Team);
        Assert.DoesNotContain("private bookmark", f.Db.Tasks.Single().CompanionJson);
        Assert.Equal("cancelled", f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team).Handoff!.Status);
        Assert.Throws<TeamOperationException>(() => f.Tasks.GetCompanionWork(f.Bob, task.Id, f.Team));
    }

    [Fact] public void Deleting_account_anonymizes_shared_knowledge_and_removes_private_bookmarks()
    {
        using var f = new Fixture(); var task = f.Create();
        f.Change(task, "savepoint_save", f.Bob, r => r.NextStep = "private");
        f.Change(task, "note_add", f.Bob, r => { r.Tried = "try"; r.Learned = "shared lesson"; });
        f.Change(task, "help_open", f.Bob, r => { r.Kind = "material"; r.Message = "asset"; });
        f.Users.DeleteProfile(f.Bob);
        var response = f.Tasks.GetCompanionWork(f.Alice, task.Id, f.Team);
        Assert.Null(response.Notes.Single().AuthorId); Assert.Equal("shared lesson", response.Notes.Single().Learned);
        Assert.Equal("cancelled", response.Help!.Status); Assert.Null(response.Help.AuthorId);
        Assert.DoesNotContain("private", f.Db.Tasks.Single().CompanionJson);
    }

    [Fact] public void Workspace_and_account_boundaries_cover_every_read()
    {
        using var f = new Fixture(); var task = f.Create();
        Assert.Throws<TeamOperationException>(() => f.Tasks.GetCompanionWork(f.Outside, task.Id, f.Team));
        Assert.Throws<TeamOperationException>(() => f.Tasks.GetCompanionDashboard(f.Outside, f.Team));
        Assert.Throws<TeamOperationException>(() => f.Tasks.FindCompanionNotes(f.Outside, null, null, null, f.Team));
        Assert.Throws<TeamOperationException>(() => f.Tasks.GetCompanionWork(f.Alice, task.Id));
        Assert.Throws<TeamOperationException>(() => f.Tasks.GetCompanionDashboard(999));
    }

    [Fact] public void Weekly_note_counts_use_JST_Monday_and_workspace_not_completion_XP()
    {
        using var f = new Fixture(); var task = f.Create();
        var work = new CompanionWorkState { Notes = [
            new() { Tried = "old", Learned = "old", CreatedAt = new(2026,9,6,14,59,59,DateTimeKind.Utc) },
            new() { Tried = "new", Learned = "new", CreatedAt = new(2026,9,6,15,0,0,DateTimeKind.Utc) },
            new() { Tried = "future", Learned = "future", CreatedAt = new(2026,9,8,15,0,0,DateTimeKind.Utc) }
        ] };
        f.Db.Tasks.Single().CompanionJson = JsonSerializer.Serialize(work); f.Db.SaveChanges();
        var report = f.Tasks.GetCompanionDashboard(f.Alice, f.Team, new DateTime(2026,9,7,16,0,0,DateTimeKind.Utc));
        Assert.Equal("2026-09-07", report.WeekStart); Assert.Equal(1, report.NotesThisWeek);
        Assert.Empty(f.Db.CompletionRewards);
    }
}
