using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public partial class TaskService
{
    public CompanionWorkResponse GetCompanionWork(int userId, int taskId, int? teamId = null)
    {
        RequireUser(userId); RequireScope(userId, teamId);
        var task = ScopedTasks(userId, teamId).AsNoTracking().SingleOrDefault(task => task.Id == taskId)
            ?? throw WorkError("task_not_found", "タスクが見つかりません。", 404);
        return WorkResponse(task, userId);
    }

    public CompanionWorkResponse ChangeCompanionWork(int userId, int taskId, CompanionWorkRequest request, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        RequireUser(userId); RequireScope(userId, teamId);
        var task = ScopedTasks(userId, teamId).SingleOrDefault(task => task.Id == taskId)
            ?? throw WorkError("task_not_found", "タスクが見つかりません。", 404);
        if (!request.Version.HasValue || request.Version != task.Version || request.ExpectedUpdatedAt != task.UpdatedAt)
            throw WorkError("work_conflict", "タスクが更新されています。入力は保持しています。「更新」で最新の内容を確認してください。", 409);
        var work = ReadWork(task);
        // Match PostgreSQL microsecond precision in the response used by the next write.
        var now = new DateTime(Math.Max(DateTime.UtcNow.Ticks / 10 * 10, task.UpdatedAt.Ticks + 10), DateTimeKind.Utc);
        var owner = IsWorkOwner(userId, teamId);
        bool CanEdit(int? author) => author == userId || owner;
        void NeedTeam()
        {
            if (!teamId.HasValue) throw WorkError("team_required", "依頼はチームのタスクで利用できます。", 400);
        }
        void CanManage(int? author)
        {
            if (!CanEdit(author)) throw WorkError("work_forbidden", "この記録は作成者またはチーム管理者が変更できます。", 403);
        }
        switch (request.Action)
        {
            case "savepoint_save":
                var next = WorkText(request.NextStep, 400, true, "次にすること");
                var summary = WorkText(request.Summary, 400, false, "ここまでの作業");
                var url = WorkUrl(request.ResourceUrl);
                work.Savepoints.RemoveAll(item => item.UserId == userId);
                work.Savepoints.Add(new() { UserId = userId, Summary = summary, NextStep = next, ResourceUrl = url, SavedAt = now });
                break;
            case "savepoint_clear":
                work.Savepoints.RemoveAll(item => item.UserId == userId);
                break;
            case "help_open":
                NeedTeam();
                if (work.Help is { Status: "open" }) throw WorkError("help_open", "未解決の依頼があります。解決または取り下げ後に送信できます。", 409);
                if (request.Kind is not ("decision" or "review" or "material" or "together")) throw WorkError("invalid_help", "依頼の種類を選んでください。", 400);
                if (request.RecipientId == userId)
                    throw WorkError("invalid_recipient", "自分以外のチームメンバーを選んでください。", 400);
                var helpRecipient = ValidateAssignee(request.RecipientId, teamId);
                work.Help = new() { AuthorId = userId, RecipientId = helpRecipient, Kind = request.Kind,
                    Message = WorkText(request.Message, 400, true, "依頼内容"), CreatedAt = now };
                break;
            case "help_offer":
                NeedTeam(); NeedHelp(work, request.EntryId);
                if (work.Help!.AuthorId == userId) throw WorkError("own_help", "自分が送った依頼は引き受けられません。", 400);
                if (work.Help.RecipientUnavailable || (work.Help.RecipientId.HasValue && work.Help.RecipientId != userId))
                    throw WorkError("work_forbidden", "この依頼を引き受けられるのは、宛先のメンバーだけです。", 403);
                if (work.Help.HelperId.HasValue && work.Help.HelperId != userId) throw WorkError("help_taken", "ほかのメンバーが対応中です。", 409);
                work.Help.HelperId = userId;
                break;
            case "help_withdraw":
                NeedTeam(); NeedHelp(work, request.EntryId);
                if (work.Help!.HelperId != userId) throw WorkError("work_forbidden", "対応を辞退できるのは、引き受けた本人だけです。", 403);
                work.Help.HelperId = null;
                break;
            case "help_resolve":
            case "help_cancel":
                NeedTeam(); NeedHelp(work, request.EntryId); CanManage(work.Help!.AuthorId);
                work.Help.Status = request.Action == "help_resolve" ? "resolved" : "cancelled";
                work.Help.ClosedAt = now;
                if (request.Action == "help_resolve")
                {
                    work.Thanks.Add(work.Help);
                    work.Thanks = work.Thanks.TakeLast(20).ToList();
                }
                break;
            case "handoff_send":
                NeedTeam();
                if (work.Handoff is { Status: "sent" }) throw WorkError("handoff_pending", "返答待ちの依頼があります。変更する場合は取り下げてください。", 409);
                if (work.Handoff is { Status: "question" }) CanManage(work.Handoff.FromUserId);
                if (request.RecipientId == userId || !request.RecipientId.HasValue)
                    throw WorkError("invalid_recipient", "自分以外のチームメンバーを選んでください。", 400);
                ValidateAssignee(request.RecipientId, teamId);
                work.Handoff = new() { FromUserId = userId, ToUserId = request.RecipientId, AssignOnAccept = request.AssignOnAccept,
                    Request = WorkText(request.Message, 400, true, "依頼内容"),
                    Criteria = WorkText(request.Criteria, 400, true, "完了の条件"), ResourceUrl = WorkUrl(request.ResourceUrl), CreatedAt = now };
                break;
            case "handoff_accept":
            case "handoff_question":
                NeedTeam(); NeedHandoff(work, request.EntryId);
                if (work.Handoff!.ToUserId != userId) throw WorkError("work_forbidden", "宛先のメンバーだけが返答できます。", 403);
                work.Handoff.Reply = WorkText(request.Message, 400, request.Action == "handoff_question", "質問内容");
                work.Handoff.Status = request.Action == "handoff_accept" ? "accepted" : "question";
                work.Handoff.RespondedAt = now;
                // Legacy handoffs default to confirmation only. Assignment changes
                // only after the sender explicitly requested it and the recipient accepts.
                if (request.Action == "handoff_accept" && work.Handoff.AssignOnAccept)
                    task.AssigneeUserProfileId = ValidateAssignee(userId, teamId);
                break;
            case "handoff_cancel":
                NeedTeam(); NeedHandoff(work, request.EntryId); CanManage(work.Handoff!.FromUserId);
                work.Handoff.Status = "cancelled"; work.Handoff.RespondedAt = now;
                break;
            case "note_add":
            case "note_edit":
                WorkNote note;
                if (request.Action == "note_add")
                {
                    if (work.Notes.Count >= 12) throw WorkError("note_limit", "作業メモは1タスクにつき12件までです。不要なメモを削除してから追加してください。", 409);
                    note = new() { AuthorId = userId, CreatedAt = now };
                }
                else
                {
                    note = work.Notes.SingleOrDefault(item => item.Id == request.EntryId) ?? throw WorkError("note_missing", "メモが見つかりません。", 404);
                    CanManage(note.AuthorId);
                }
                note.Tried = WorkText(request.Tried, 400, false, "補足・試したこと");
                note.Learned = WorkText(request.Learned, 400, true, "メモ本文");
                note.NextStep = WorkText(request.NextStep, 400, false, "次に試すこと");
                note.ResourceUrl = WorkUrl(request.ResourceUrl); note.UpdatedAt = now;
                if (request.Action == "note_add") work.Notes.Add(note);
                break;
            case "note_delete":
                var old = work.Notes.SingleOrDefault(item => item.Id == request.EntryId) ?? throw WorkError("note_missing", "メモが見つかりません。", 404);
                CanManage(old.AuthorId); work.Notes.Remove(old);
                break;
            case "showcase_save":
                if (task.Status != TaskItemStatus.Done) throw WorkError("not_completed", "作品棚にはDONEのタスクを飾れます。", 409);
                if (work.Showcase != null) CanManage(work.Showcase.AuthorId);
                if (request.Kind is not ("frame" or "monitor" or "book")) throw WorkError("invalid_showcase", "額縁・モニター・本から選んでください。", 400);
                work.Showcase = new() { AuthorId = work.Showcase?.AuthorId ?? userId, Kind = request.Kind,
                    Title = WorkText(request.Title, 120, true, "作品名"), Outcome = WorkText(request.Summary, 400, true, "できたこと"),
                    ResourceUrl = WorkUrl(request.ResourceUrl), CreatedAt = work.Showcase?.CreatedAt ?? now, UpdatedAt = now };
                break;
            case "showcase_remove":
                if (work.Showcase != null) CanManage(work.Showcase.AuthorId);
                work.Showcase = null;
                break;
            default: throw WorkError("invalid_action", "操作の種類が不正です。", 400);
        }
        // Detached JSON is validated in full before mutating the tracked task.
        var json = JsonSerializer.Serialize(work);
        if (json.Length > 120000) throw WorkError("work_limit", "記録の容量上限です。不要なノートを整理してください。", 409);
        task.CompanionJson = json;
        task.UpdatedAt = now;
        _context.SaveChanges();
        var response = WorkResponse(task, userId);
        scope.Commit();
        return response;
    }

    public CompanionDashboard GetCompanionDashboard(int userId, int? teamId = null, DateTime? utcNow = null)
    {
        RequireUser(userId); RequireScope(userId, teamId);
        var now = utcNow ?? DateTime.UtcNow;
        var local = now.AddHours(9);
        var weekStart = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        var startUtc = DateTime.SpecifyKind(weekStart.AddHours(-9), DateTimeKind.Utc);
        var people = WorkPeople(userId, teamId);
        var isOwner = IsWorkOwner(userId, teamId);
        var records = ScopedTasks(userId, teamId).AsNoTracking().Where(task => task.CompanionJson != "{}").ToList()
            .Select(task => WorkResponse(task, userId, people, isOwner)).ToList();
        var notes = records.SelectMany(task => task.Notes.Select(note => new WorkNoteMatch(task.TaskId, task.TaskTitle, note,
            people.GetValueOrDefault(note.AuthorId ?? 0, "退出・退会したメンバー"), "最近の記録"))).ToList();
        return new(weekStart.ToString("yyyy-MM-dd"), notes.Count(item => item.Note.CreatedAt >= startUtc && item.Note.CreatedAt <= now),
            records.Where(item => item.Savepoint != null && item.TaskStatus != TaskItemStatus.Done).OrderByDescending(item => item.Savepoint!.SavedAt).Take(20).ToList(),
            records.Where(item => item.Help?.Status == "open").OrderByDescending(item => item.Help!.CreatedAt).Take(20).ToList(),
            records.Where(item => item.Handoff?.Status is "sent" or "question").OrderByDescending(item => item.Handoff!.CreatedAt).Take(20).ToList(),
            records.Where(item => item.Showcase != null && item.TaskStatus == TaskItemStatus.Done).OrderByDescending(item => item.Showcase!.UpdatedAt).Take(50).ToList(),
            notes.OrderByDescending(item => item.Note.UpdatedAt).Take(10).ToList())
        {
            RecentThanks = records.SelectMany(item => item.Thanks.Select(thanks => new WorkThanksMatch(item.TaskId, item.TaskTitle,
                people.GetValueOrDefault(thanks.AuthorId ?? 0, "退出・退会したメンバー"),
                thanks.HelperId.HasValue ? people.GetValueOrDefault(thanks.HelperId.Value, "退出・退会したメンバー") : "チームのみんな", thanks.ClosedAt)))
                .OrderByDescending(item => item.ClosedAt).Take(10).ToList()
        };
    }

    public List<WorkNoteMatch> FindCompanionNotes(int userId, string? query, string? tags, int? excludeTaskId, int? teamId = null)
    {
        RequireUser(userId); RequireScope(userId, teamId);
        var search = WorkText(query, 200, false, "検索文字");
        if (tags?.Length > 300) throw WorkError("invalid_search", "タグは300文字までです。", 400);
        var tokens = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray();
        var tagNames = TaskTagService.Tokenize(tags);
        var people = WorkPeople(userId, teamId);
        var results = new List<(int Score, WorkNoteMatch Match)>();
        foreach (var task in ScopedTasks(userId, teamId).AsNoTracking().Where(task => task.Id != excludeTaskId && task.CompanionJson != "{}").ToList())
        {
            var tagMatch = TaskTagService.Tokenize(task.Tags).Intersect(tagNames, StringComparer.OrdinalIgnoreCase).Any();
            foreach (var note in ReadWork(task).Notes)
            {
                var haystack = $"{task.Title}\n{note.Tried}\n{note.Learned}\n{note.NextStep}";
                var matches = tokens.Count(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (tagNames.Count + tokens.Length > 0 && !tagMatch && matches == 0) continue;
                results.Add((matches + (tagMatch ? 10 : 0), new(task.Id, task.Title, note,
                    people.GetValueOrDefault(note.AuthorId ?? 0, "退出・退会したメンバー"), tagMatch ? "同じタグの記録" : matches > 0 ? "キーワードが一致" : "最近の記録")));
            }
        }
        return results.OrderByDescending(item => item.Score).ThenByDescending(item => item.Match.Note.UpdatedAt).Take(10).Select(item => item.Match).ToList();
    }

    private static CompanionWorkState ReadWork(TaskItem task) => JsonSerializer.Deserialize<CompanionWorkState>(task.CompanionJson) ?? new();
    private bool IsWorkOwner(int userId, int? teamId) => teamId.HasValue && _context.Teams.Any(team => team.Id == teamId && team.OwnerUserProfileId == userId);
    private Dictionary<int, string> WorkPeople(int userId, int? teamId)
    {
        var ids = teamId.HasValue ? _context.TeamMembers.Where(item => item.TeamId == teamId).Select(item => item.UserProfileId).ToList() : [userId];
        return _context.UserProfiles.AsNoTracking().Where(user => ids.Contains(user.Id)).ToDictionary(user => user.Id, user => user.DisplayName);
    }
    private CompanionWorkResponse WorkResponse(TaskItem task, int userId, Dictionary<int, string>? people = null, bool? isOwner = null)
    {
        var work = ReadWork(task);
        return new(task.Id, task.Title, task.TeamId, task.Status, task.Tags, task.Version, task.UpdatedAt, userId,
            isOwner ?? IsWorkOwner(userId, task.TeamId), people ?? WorkPeople(userId, task.TeamId),
            work.Savepoints.SingleOrDefault(item => item.UserId == userId), work.Help, work.Handoff, work.Notes, work.Showcase) { Thanks = work.Thanks };
    }
    private static TeamOperationException WorkError(string code, string message, int status) => new(code, message, status);
    private static string WorkText(string? value, int maximum, bool required, string label)
    {
        var text = value?.Trim() ?? "";
        if ((required && text.Length == 0) || text.Length > maximum || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw WorkError("invalid_work_text", $"{label}は{(required ? "1" : "0")}〜{maximum}文字で入力してください。", 400);
        return text;
    }
    private static string? WorkUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > 1000 || text.Any(char.IsControl) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host))
            throw WorkError("invalid_work_url", "リンクは1000文字以内のhttp/https URLにしてください。認証情報入りのURLは保存できません。", 400);
        return text;
    }
    private static void NeedHelp(CompanionWorkState work, Guid? id)
    {
        if (work.Help == null || work.Help.Id != id || work.Help.Status != "open") throw WorkError("help_changed", "依頼は変更されたか、すでに終了しています。", 409);
    }
    private static void NeedHandoff(CompanionWorkState work, Guid? id)
    {
        if (work.Handoff == null || work.Handoff.Id != id || work.Handoff.Status is not ("sent" or "question")) throw WorkError("handoff_changed", "依頼は変更されたか、すでに終了しています。", 409);
    }

    // Called under the same workspace write lock on leave/account deletion. Shared
    // knowledge remains; private bookmarks are removed and outstanding requests closed.
    public static void RemoveCompanionUser(AppDbContext context, int userId, int? teamId = null, bool deletingAccount = false)
    {
        var tasks = context.Tasks.Where(task => task.TeamId != null && (!teamId.HasValue || task.TeamId == teamId)).ToList();
        foreach (var task in tasks)
        {
            if (ScrubCompanionUser(task, userId, deletingAccount)) task.UpdatedAt = DateTime.UtcNow;
        }
        // Short-lived deleted-task snapshots must not resurrect private bookmarks.
        foreach (var entry in context.TaskUndoEntries.Where(entry => !teamId.HasValue || entry.TeamId == teamId).ToList())
        {
            var task = JsonSerializer.Deserialize<TaskItem>(entry.Snapshot)!;
            if (ScrubCompanionUser(task, userId, deletingAccount)) entry.Snapshot = JsonSerializer.Serialize(task);
        }
    }

    private static bool ScrubCompanionUser(TaskItem task, int userId, bool deletingAccount)
    {
        if (task.CompanionJson == "{}") return false;
        var work = ReadWork(task);
        work.Savepoints.RemoveAll(item => item.UserId == userId);
        if (work.Help is { } help)
        {
            if ((help.AuthorId == userId || help.RecipientId == userId) && help.Status == "open")
            { help.Status = "cancelled"; help.ClosedAt = DateTime.UtcNow; }
            if (help.RecipientId == userId)
            {
                help.RecipientUnavailable = true;
                if (deletingAccount) help.RecipientId = null;
            }
            if (help.HelperId == userId) help.HelperId = null;
            if (help.AuthorId == userId && deletingAccount) help.AuthorId = null;
        }
        if (work.Handoff is { } handoff)
        {
            if ((handoff.FromUserId == userId || handoff.ToUserId == userId) && handoff.Status is "sent" or "question")
            { handoff.Status = "cancelled"; handoff.RespondedAt = DateTime.UtcNow; }
            if (deletingAccount) { if (handoff.FromUserId == userId) handoff.FromUserId = null; if (handoff.ToUserId == userId) handoff.ToUserId = null; }
        }
        if (deletingAccount)
        {
            foreach (var thanks in work.Thanks)
            {
                if (thanks.AuthorId == userId) thanks.AuthorId = null;
                if (thanks.HelperId == userId) thanks.HelperId = null;
                if (thanks.RecipientId == userId) { thanks.RecipientId = null; thanks.RecipientUnavailable = true; }
            }
            foreach (var note in work.Notes.Where(note => note.AuthorId == userId)) note.AuthorId = null;
            if (work.Showcase?.AuthorId == userId) work.Showcase.AuthorId = null;
        }
        var json = JsonSerializer.Serialize(work);
        if (json == task.CompanionJson) return false;
        task.CompanionJson = json; return true;
    }
}
