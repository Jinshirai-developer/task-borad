using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public partial class TaskService
{
    private int? ValidateAssignee(int? assignee, int? teamId)
    {
        if (assignee == null) return null;
        if (teamId == null || !_context.TeamMembers.Any(member => member.TeamId == teamId && member.UserProfileId == assignee))
            throw new TeamOperationException("invalid_assignee", "担当者はこのチームに参加中のメンバーから選んでください。", 400);
        return assignee;
    }

    private static string SerializeChecklist(List<ChecklistItem>? items)
    {
        if (items == null || items.Count > 20 || items.Any(item => item == null
            || string.IsNullOrWhiteSpace(item.Text) || item.Text.Trim().Length > 120 || item.Text.Any(char.IsControl)))
            throw new TeamOperationException("invalid_checklist", "チェックリストは20項目まで、各項目は1〜120文字で入力してください。", 400);
        return JsonSerializer.Serialize(items.Select(item => item with { Text = item.Text.Trim() }).ToList());
    }

    private UndoReceipt SaveUndo(int userId, TaskItem task, string snapshot, bool deleted, int? rewardId = null)
    {
        var now = DateTime.UtcNow;
        _context.TaskUndoEntries.RemoveRange(_context.TaskUndoEntries.Where(entry => entry.ExpiresAt <= now));
        var oldEntries = _context.TaskUndoEntries.Where(entry => entry.UserProfileId == userId)
            .OrderByDescending(entry => entry.ExpiresAt).Skip(19).ToList();
        _context.TaskUndoEntries.RemoveRange(oldEntries);
        var entry = new TaskUndoEntry
        {
            UserProfileId = userId, TeamId = task.TeamId, TaskId = task.Id,
            WasDeleted = deleted, Snapshot = snapshot, RewardId = rewardId,
            ExpectedVersion = task.Version, ExpectedUpdatedAt = task.UpdatedAt,
            ExpiresAt = now.AddSeconds(30)
        };
        _context.TaskUndoEntries.Add(entry);
        _context.SaveChanges();
        return new UndoReceipt(entry.Id, entry.ExpiresAt);
    }

    public UndoReceipt? DeleteWithUndo(int userId, int id, uint? version, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        RequireUser(userId);
        RequireScope(userId, teamId);
        var task = ScopedTasks(userId, teamId).SingleOrDefault(item => item.Id == id);
        if (task == null) return null;
        RequireVersion(task, version);
        var snapshot = JsonSerializer.Serialize(task);
        var rewardId = _context.CompletionRewards.Where(item => item.TaskId == id).Select(item => (int?)item.Id).SingleOrDefault();
        Delete(userId, id, version, teamId);
        var receipt = SaveUndo(userId, task, snapshot, true, rewardId);
        scope.Commit();
        return receipt;
    }

    public TaskResponse Undo(int userId, Guid token, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        RequireUser(userId);
        RequireScope(userId, teamId);
        var entry = _context.TaskUndoEntries.SingleOrDefault(item => item.Id == token
            && item.UserProfileId == userId && item.TeamId == teamId && item.ExpiresAt > DateTime.UtcNow)
            ?? throw new TeamOperationException("undo_expired", "元に戻せる時間を過ぎたか、すでに取り消されています。", 409);
        var before = JsonSerializer.Deserialize<TaskItem>(entry.Snapshot)!;
        TaskResponse response;
        if (!entry.WasDeleted)
        {
            var current = ScopedTasks(userId, teamId).SingleOrDefault(item => item.Id == entry.TaskId);
            if (current == null || current.Version != entry.ExpectedVersion || current.UpdatedAt != entry.ExpectedUpdatedAt)
                throw new TeamOperationException("undo_conflict", "このタスクは別の操作で変更されています。最新の内容を確認してください。", 409);
            // Undo status only. Reopening someone else's DONE and then undoing
            // must restore the original recipient, never transfer XP to this actor.
            var now = DateTime.UtcNow;
            if (current.Status == TaskItemStatus.Done && before.Status != TaskItemStatus.Done)
                _petService.RevokeTaskCompletionReward(current, now);
            else if (current.Status != TaskItemStatus.Done && before.Status == TaskItemStatus.Done)
                _petService.RestoreRevokedTaskCompletionReward(current, before.CompletionRewardedAt, now);
            current.Status = before.Status;
            current.IsCompleted = before.IsCompleted;
            current.UpdatedAt = now;
            _context.SaveChanges();
            response = ToResponse(current);
        }
        else
        {
            if (_context.Tasks.Any(task => task.Id == entry.TaskId))
                throw new TeamOperationException("undo_conflict", "このタスクはすでに復元されています。", 409);
            if (ScopedTasks(userId, teamId).Count() >= MaximumTasksPerUser)
                throw new TaskLimitExceededException(MaximumTasksPerUser, teamId.HasValue);
            if (before.UserProfileId is { } creator && !_context.UserProfiles.Any(user => user.Id == creator))
                before.UserProfileId = null;
            if (before.AssigneeUserProfileId is { } assignee && !_context.TeamMembers.Any(member => member.TeamId == teamId && member.UserProfileId == assignee))
                before.AssigneeUserProfileId = null;
            before.Version = 0;
            before.UpdatedAt = DateTime.UtcNow;
            _context.Tasks.Add(before);
            // Delete intentionally preserves earned XP. Reattach its existing receipt,
            // never call ApplyTaskCompletionReward when restoring a deleted task.
            if (entry.RewardId is { } rewardId)
            {
                var reward = _context.CompletionRewards.SingleOrDefault(item => item.Id == rewardId);
                if (reward != null && reward.TaskId == null) { reward.Task = before; reward.TaskId = before.Id; }
            }
            _context.SaveChanges();
            response = ToResponse(before);
        }
        _context.TaskUndoEntries.Remove(entry);
        _context.SaveChanges();
        scope.Commit();
        return response;
    }
}
