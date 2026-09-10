using Microsoft.EntityFrameworkCore;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public partial class TaskService
{
    public WorkInboxResponse GetWorkInbox(int userId, string view = "incoming")
    {
        RequireUser(userId);
        if (view is not ("incoming" or "helping" or "sent"))
            throw new TeamOperationException("invalid_inbox_view", "依頼の表示条件が正しくありません。", 400);
        // Membership is checked on every read, including when the UI is in a personal workspace.
        var memberships = _context.TeamMembers.Where(member => member.UserProfileId == userId).Select(member => member.TeamId);
        var teams = _context.Teams.AsNoTracking().Where(team => memberships.Contains(team.Id))
            .ToDictionary(team => team.Id, team => team.Name);
        var ids = teams.Keys.ToList();
        var memberIds = _context.TeamMembers.Where(member => ids.Contains(member.TeamId)).Select(member => member.UserProfileId);
        var people = _context.UserProfiles.AsNoTracking().Where(user => memberIds.Contains(user.Id)).ToDictionary(user => user.Id, user => user.DisplayName);
        string Person(int? id) => id.HasValue && people.TryGetValue(id.Value, out var name) ? name : "退出したメンバー";
        var tasks = _context.Tasks.AsNoTracking().Where(task => task.TeamId.HasValue
            && ids.Contains(task.TeamId.Value) && task.CompanionJson != "{}").ToList();
        var incoming = new List<WorkInboxItem>();
        var helping = new List<WorkInboxItem>();
        var sent = new List<WorkInboxItem>();
        foreach (var task in tasks)
        {
            var work = ReadWork(task);
            if (work.Help is { Status: "open", RecipientUnavailable: false } help)
            {
                var item = new WorkInboxItem(help.Id, task.TeamId!.Value, teams[task.TeamId.Value], task.Id, task.Title,
                    "help", help.RecipientId == null ? "team" : "direct", help.Message, help.CreatedAt)
                {
                    AuthorName = Person(help.AuthorId), RecipientName = help.RecipientId == null ? "チーム全員" : Person(help.RecipientId),
                    HelperName = help.HelperId.HasValue ? Person(help.HelperId) : null, RequestKind = help.Kind,
                    Status = help.HelperId.HasValue ? "helping" : "waiting", Version = task.Version, UpdatedAt = task.UpdatedAt
                };
                if (help.HelperId == null && help.AuthorId != userId && (help.RecipientId == userId || help.RecipientId == null))
                    incoming.Add(item with { Actions = ["help_offer"] });
                if (help.HelperId == userId) helping.Add(item with { Actions = ["help_withdraw"] });
                if (help.AuthorId == userId) sent.Add(item with { Actions = ["help_resolve", "help_cancel"] });
            }
            if (work.Handoff is { Status: "sent" or "question" } handoff)
            {
                var item = new WorkInboxItem(handoff.Id, task.TeamId!.Value, teams[task.TeamId.Value], task.Id, task.Title,
                    "handoff", "direct", handoff.Status == "question" ? handoff.Reply ?? handoff.Request : handoff.Request,
                    handoff.RespondedAt ?? handoff.CreatedAt)
                {
                    AuthorName = Person(handoff.FromUserId), RecipientName = Person(handoff.ToUserId), Status = handoff.Status,
                    AssignOnAccept = handoff.AssignOnAccept, Criteria = handoff.Criteria, Version = task.Version, UpdatedAt = task.UpdatedAt
                };
                if (handoff.Status == "sent" && handoff.ToUserId == userId)
                    incoming.Add(item with { Actions = ["handoff_accept"] });
                if (handoff.FromUserId == userId)
                {
                    sent.Add(item with { Actions = ["handoff_cancel"] });
                    if (handoff.Status == "question") incoming.Add(item);
                }
            }
        }
        var items = view == "helping" ? helping : view == "sent" ? sent : incoming;
        return new(items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id).ToList(),
            incoming.Count(item => item.Audience == "direct"), incoming.Count(item => item.Audience == "team"))
        { HelpingCount = helping.Count, SentCount = sent.Count };
    }
}
