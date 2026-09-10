using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public sealed class TeamService(AppDbContext context)
{
    public const int MaximumMemberships = 10;
    public const int MaximumMembers = 3;

    public List<TeamResponse> GetAll(int userId)
    {
        var teams = context.Teams.AsNoTracking()
            .Where(team => context.TeamMembers.Any(member => member.TeamId == team.Id && member.UserProfileId == userId))
            .OrderBy(team => team.CreatedAt).ThenBy(team => team.Id).ToList();
        return teams.Select(team => ToDetail(team, userId)).Cast<TeamResponse>().ToList();
    }

    public TeamDetailResponse Get(int userId, int teamId) => ToDetail(RequireMember(userId, teamId), userId);

    public TeamSummaryResponse GetSummary(int userId, int teamId)
    {
        RequireMember(userId, teamId);
        return Summarize(teamId);
    }

    public TeamCreatedResponse Create(int userId, CreateTeamRequest request)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        RequireUser(userId);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            throw new TeamOperationException("invalid_team_name", "チーム名を1〜100文字で入力してください。", 400);
        RequireMembershipCapacity(userId);
        var inviteCode = NewInviteCode();
        var team = new Team
        {
            Name = request.Name.Trim(), OwnerUserProfileId = userId,
            InviteCodeHash = HashInviteCode(inviteCode), InviteExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        context.Teams.Add(team);
        context.SaveChanges();
        context.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserProfileId = userId });
        context.SaveChanges();
        var result = new TeamCreatedResponse(ToDetail(team, userId), inviteCode, team.InviteExpiresAt);
        scope.Commit();
        return result;
    }

    public TeamDetailResponse Join(int userId, JoinTeamRequest request)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        RequireUser(userId);
        var code = request.InviteCode.Trim();
        if (code.Length is < 20 or > 100) throw InvalidInvite();
        var hash = HashInviteCode(code);
        var team = context.Teams.SingleOrDefault(item => item.InviteCodeHash == hash && item.InviteExpiresAt > DateTime.UtcNow);
        if (team == null) throw InvalidInvite();
        if (context.TeamMembers.Any(member => member.TeamId == team.Id && member.UserProfileId == userId))
        {
            var current = ToDetail(team, userId);
            scope.Commit();
            return current;
        }
        RequireMembershipCapacity(userId);
        var billing = context.TeamBillings.SingleOrDefault(item => item.UserProfileId == team.OwnerUserProfileId);
        if (billing?.HasPro(DateTime.UtcNow) != true && context.TeamMembers.Count(member => member.TeamId == team.Id) >= MaximumMembers)
            throw new TeamOperationException("team_full", $"無料チームは所有者を含め{MaximumMembers}人までです。所有者にアカウントPro（テスト）の利用を相談してください。既存メンバーは引き続き利用できます。");
        context.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserProfileId = userId });
        context.SaveChanges();
        var result = ToDetail(team, userId);
        scope.Commit();
        return result;
    }

    public TeamInviteResponse RotateInvite(int userId, int teamId)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var team = RequireOwner(userId, teamId);
        var code = NewInviteCode();
        team.InviteCodeHash = HashInviteCode(code);
        team.InviteExpiresAt = DateTime.UtcNow.AddDays(7);
        context.SaveChanges();
        scope.Commit();
        return new TeamInviteResponse(code, team.InviteExpiresAt);
    }

    public void Leave(int userId, int teamId)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var team = RequireMember(userId, teamId);
        if (team.OwnerUserProfileId == userId)
            throw new TeamOperationException("team_owner", "オーナーはそのまま退出できません。他のメンバーへ所有権を移譲するか、チームを削除してください。");
        var member = context.TeamMembers.Single(item => item.TeamId == teamId && item.UserProfileId == userId);
        TaskService.RemoveCompanionUser(context, userId, teamId);
        foreach (var task in context.Tasks.Where(task => task.TeamId == teamId && task.AssigneeUserProfileId == userId))
        {
            task.AssigneeUserProfileId = null;
            task.UpdatedAt = DateTime.UtcNow;
        }
        context.TaskUndoEntries.RemoveRange(context.TaskUndoEntries.Where(item => item.TeamId == teamId && item.UserProfileId == userId));
        context.TeamMembers.Remove(member);
        context.SaveChanges();
        scope.Commit();
    }

    public TeamDetailResponse TransferOwner(int userId, int teamId, int nextOwnerId)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var team = RequireOwner(userId, teamId);
        if (!context.TeamMembers.Any(member => member.TeamId == teamId && member.UserProfileId == nextOwnerId))
            throw new TeamOperationException("member_required", "所有権はこのチームのメンバーにのみ移譲できます。", 400);
        team.OwnerUserProfileId = nextOwnerId;
        // The outgoing owner cannot retain an invitation credential after handing over the team.
        team.InviteCodeHash = HashInviteCode(NewInviteCode());
        team.InviteExpiresAt = DateTime.UtcNow;
        context.SaveChanges();
        var result = ToDetail(team, userId);
        scope.Commit();
        return result;
    }

    public void Delete(int userId, int teamId)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var team = RequireOwner(userId, teamId);
        // Explicit removal keeps the behavior identical in the non-relational test provider.
        var tasks = context.Tasks.Where(task => task.TeamId == teamId).ToList();
        var taskIds = tasks.Select(task => task.Id).ToList();
        foreach (var reward in context.CompletionRewards.Where(reward => reward.TaskId.HasValue && taskIds.Contains(reward.TaskId.Value)))
        {
            reward.Task = null;
            reward.TaskId = null;
        }
        context.Tasks.RemoveRange(tasks);
        context.TaskUndoEntries.RemoveRange(context.TaskUndoEntries.Where(item => item.TeamId == teamId));
        context.TaskTagDefinitions.RemoveRange(context.TaskTagDefinitions.Where(tag => tag.TeamId == teamId));
        context.TeamMembers.RemoveRange(context.TeamMembers.Where(member => member.TeamId == teamId));
        // An account subscription survives deleting any/all of its teams.
        context.Teams.Remove(team);
        context.SaveChanges();
        scope.Commit();
    }

    public Team RequireMember(int userId, int teamId)
    {
        var team = context.Teams.SingleOrDefault(item => item.Id == teamId
            && context.TeamMembers.Any(member => member.TeamId == item.Id && member.UserProfileId == userId));
        return team ?? throw new TeamOperationException("team_not_found", "チームが見つからないか、参加していません。", 404);
    }

    private Team RequireOwner(int userId, int teamId)
    {
        var team = RequireMember(userId, teamId);
        if (team.OwnerUserProfileId != userId)
            throw new TeamOperationException("owner_required", "この操作はチームのオーナーのみ行えます。", 403);
        return team;
    }

    private void RequireUser(int userId)
    {
        if (!context.UserProfiles.Any(user => user.Id == userId))
            throw new TeamOperationException("account_not_found", "アカウントが見つかりません。再ログインしてください。", 401);
    }

    private void RequireMembershipCapacity(int userId)
    {
        if (context.TeamMembers.Count(member => member.UserProfileId == userId) >= MaximumMemberships)
            throw new TeamOperationException("membership_limit", $"参加できるチームは{MaximumMemberships}件までです。");
    }

    private TeamDetailResponse ToDetail(Team team, int userId)
    {
        var members = (from member in context.TeamMembers.AsNoTracking()
                       join user in context.UserProfiles.AsNoTracking() on member.UserProfileId equals user.Id
                       where member.TeamId == team.Id
                       orderby member.JoinedAt, member.UserProfileId
                       select new TeamMemberResponse(user.Id, user.DisplayName,
                           user.Id == team.OwnerUserProfileId ? "owner" : "member")).ToList();
        var counts = Summarize(team.Id);
        return new TeamDetailResponse
        {
            Id = team.Id, Name = team.Name, OwnerUserProfileId = team.OwnerUserProfileId,
            Role = team.OwnerUserProfileId == userId ? "owner" : "member", Members = members,
            MemberCount = members.Count, TotalTasks = counts.Total, TodoCount = counts.Todo,
            DoingCount = counts.Doing, DoneCount = counts.Done, OverdueCount = counts.Overdue
        };
    }

    private TeamSummaryResponse Summarize(int teamId)
    {
        var tasks = context.Tasks.AsNoTracking().Where(task => task.TeamId == teamId);
        // Due dates are calendar dates encoded as UTC midnight, not instants. Match
        // the Japanese UI calendar so an item due today is not overdue during today.
        var today = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9).Date, DateTimeKind.Utc);
        var counts = tasks.GroupBy(_ => 1).Select(group => new TeamSummaryResponse(
            group.Count(), group.Count(task => task.Status == TaskItemStatus.Todo),
            group.Count(task => task.Status == TaskItemStatus.Doing), group.Count(task => task.Status == TaskItemStatus.Done),
            group.Count(task => task.Status != TaskItemStatus.Done && task.DueDate < today))).SingleOrDefault();
        return counts ?? new TeamSummaryResponse(0, 0, 0, 0, 0);
    }

    private static string NewInviteCode() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string HashInviteCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    private static TeamOperationException InvalidInvite() => new("invalid_invite", "招待コードが無効か、有効期限が切れています。", 400);
}

public sealed class TeamOperationException(string code, string message, int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
