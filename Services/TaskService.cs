using TaskApi.Data;
using TaskApi.Models;
using TaskApi.DTOs;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TaskApi.Services;
public partial class TaskService
{
    private const int MaximumTasksPerUser = 500;

    private readonly AppDbContext _context;
    private readonly ILogger<TaskService> _logger;
    private readonly PetService _petService;
    private readonly UserProfileService _userProfileService;

    public TaskService(AppDbContext context, ILogger<TaskService> logger, PetService petService, UserProfileService userProfileService)
    {
        _context = context;
        _logger = logger;
        _petService = petService;
        _userProfileService = userProfileService;
    }

    // The string-key overloads retain compatibility with local service callers. HTTP endpoints
    // always use authenticated numeric IDs and never create a missing account.
    public PagedResponse<TaskResponse> GetAll(string? userKey, bool? isCompleted, TaskItemStatus? status, TaskPriority? priority, string? tag, string? sortOrder, string? search, int page, int pageSize, int? teamId = null, string? tagExact = null, bool untagged = false) =>
        GetAll(_userProfileService.GetOrCreateByKey(userKey).Id, isCompleted, status, priority, tag, sortOrder, search, page, pageSize, teamId, tagExact, untagged);

    public PagedResponse<TaskResponse> GetAll(int userId, bool? isCompleted, TaskItemStatus? status, TaskPriority? priority, string? tag, string? sortOrder, string? search, int page, int pageSize, int? teamId = null, string? tagExact = null, bool untagged = false, string? assignee = null, string? due = null)
    {
        RequireUser(userId);
        RequireScope(userId, teamId);
        if (tagExact != null && (tagExact.Length > 300 || string.IsNullOrWhiteSpace(tagExact)))
            throw new TeamOperationException("invalid_exact_tag", "tagExactは1〜300文字のタグ名を指定してください。", 400);
        if (untagged && tagExact != null)
            throw new TeamOperationException("invalid_tag_filters", "tagExactとuntaggedは同時に指定できません。", 400);
        var query = ScopedTasks(userId, teamId).AsNoTracking();
        if (!string.IsNullOrEmpty(assignee))
        {
            if (assignee == "me") query = teamId.HasValue
                ? query.Where(task => task.AssigneeUserProfileId == userId) : query;
            else if (assignee == "unassigned") query = query.Where(task => task.AssigneeUserProfileId == null);
            else if (int.TryParse(assignee, out var assigneeId) && teamId.HasValue
                && _context.TeamMembers.Any(member => member.TeamId == teamId && member.UserProfileId == assigneeId))
                query = query.Where(task => task.AssigneeUserProfileId == assigneeId);
            else throw new TeamOperationException("invalid_assignee_filter", "担当者は選択中のチームのメンバーから選んでください。", 400);
        }
        var today = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9).Date, DateTimeKind.Utc);
        query = due switch
        {
            null or "" => query,
            "today" => query.Where(task => task.Status != TaskItemStatus.Done && task.DueDate == today),
            "through_today" => query.Where(task => task.Status != TaskItemStatus.Done && task.DueDate <= today),
            "overdue" => query.Where(task => task.Status != TaskItemStatus.Done && task.DueDate < today),
            "none" => query.Where(task => task.DueDate == null),
            _ => throw new TeamOperationException("invalid_due_filter", "期限の検索条件が不正です。", 400)
        };

        if (status.HasValue)
        {
            query = query.Where(task => task.Status == status.Value);
        }
        else if (isCompleted.HasValue)
        {
            query = query.Where(task => task.IsCompleted == isCompleted.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(task => task.Title.Contains(search) || (task.Description != null && task.Description.Contains(search)));
        }

        if (priority.HasValue)
        {
            query = query.Where(task => task.Priority == priority.Value);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(task => task.Tags != null && task.Tags.Contains(tag.Trim()));
        }

        int totalCount;
        TaskStatusCounts statusCounts;
        List<TaskItem> tasks;
        if (tagExact != null || untagged)
        {
            // The workspace has at most 500 tasks. Tokenize before counting/paging,
            // avoiding provider-specific SQL split/collation differences and partial
            // matches such as "art" accidentally matching "artist".
            var exactName = tagExact?.Trim();
            var matching = query.ToList().Where(task => untagged
                ? TaskTagService.Tokenize(task.Tags).Count == 0
                : TaskTagService.Tokenize(task.Tags).Contains(exactName!, StringComparer.OrdinalIgnoreCase)).ToList();
            totalCount = matching.Count;
            statusCounts = new(matching.Count(task => task.Status == TaskItemStatus.Todo),
                matching.Count(task => task.Status == TaskItemStatus.Doing), matching.Count(task => task.Status == TaskItemStatus.Done));
            var ordered = OrderTasks(matching.AsQueryable(), sortOrder);
            tasks = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            totalCount = query.Count();
            var counts = query.GroupBy(task => task.Status).Select(group => new { Status = group.Key, Count = group.Count() })
                .ToDictionary(item => item.Status, item => item.Count);
            statusCounts = new(counts.GetValueOrDefault(TaskItemStatus.Todo), counts.GetValueOrDefault(TaskItemStatus.Doing), counts.GetValueOrDefault(TaskItemStatus.Done));
            var ordered = OrderTasks(query, sortOrder);
            tasks = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        var creatorIds = tasks.Where(task => task.UserProfileId.HasValue).Select(task => task.UserProfileId!.Value).Distinct().ToList();
        var creatorNames = _context.UserProfiles.AsNoTracking().Where(user => creatorIds.Contains(user.Id))
            .ToDictionary(user => user.Id, user => user.DisplayName);
        var items = tasks.Select(task => ToResponse(task, creatorNames)).ToList();

        return new PagedResponse<TaskResponse>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            StatusCounts = statusCounts
        };
    }
    private static IOrderedQueryable<TaskItem> OrderTasks(IQueryable<TaskItem> query, string? sortOrder) => sortOrder switch
    {
        "asc" => query.OrderBy(task => task.CreatedAt).ThenBy(task => task.Id),
        "due" => query.OrderBy(task => task.DueDate == null).ThenBy(task => task.DueDate)
            .ThenByDescending(task => task.Priority).ThenByDescending(task => task.Id),
        _ => query.OrderByDescending(task => task.CreatedAt).ThenByDescending(task => task.Id)
    };

    public TaskResponse? GetById(string? userKey, int id, int? teamId = null) =>
        GetById(_userProfileService.GetOrCreateByKey(userKey).Id, id, teamId);

    public TaskResponse? GetById(int userId, int id, int? teamId = null)
    {
        RequireUser(userId);
        RequireScope(userId, teamId);
        var task = ScopedTasks(userId, teamId).AsNoTracking().SingleOrDefault(task => task.Id == id);

        if (task == null)
        {
            _logger.LogWarning("Task not found with ID: {Id}", id);
            return null;
        }

        return ToResponse(task);
    }
    public TaskResponse Create(string? userKey, CreateTaskRequest request, int? teamId = null) =>
        Create(_userProfileService.GetOrCreateByKey(userKey).Id, request, teamId);

    public TaskResponse Create(int userId, CreateTaskRequest request, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        var user = RequireUser(userId);
        RequireScope(userId, teamId);

        if (ScopedTasks(userId, teamId).Count() >= MaximumTasksPerUser)
        {
            throw new TaskLimitExceededException(MaximumTasksPerUser, teamId.HasValue);
        }

        var status = ResolveStatus(request.IsCompleted, request.Status);
        var now = DateTime.UtcNow;
        var task = new TaskItem
        {
            UserProfileId = user.Id,
            TeamId = teamId,
            AssigneeUserProfileId = ValidateAssignee(request.AssigneeUserProfileId, teamId),
            ChecklistJson = SerializeChecklist(request.Checklist),
            Title = request.Title,
            Description = request.Description,
            IsCompleted = IsDone(status),
            Status = status,
            DueDate = NormalizeDueDate(request.DueDate),
            Priority = request.Priority,
            Tags = NormalizeTags(request.Tags),
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Tasks.Add(task);
        if (IsDone(status))
        {
            _petService.ApplyTaskCompletionReward(task, task.AssigneeUserProfileId ?? user.Id, now);
        }

        _context.SaveChanges();
        if (IsDone(status)) PetCollectionService.RecordSavedMilestones(_context, task.AssigneeUserProfileId ?? user.Id, now);
        var response = ToResponse(task);
        scope.Commit();

        _logger.LogInformation("Task created. Id: {TaskId}", task.Id);

        return response;
    }
    public TaskResponse? Update(string? userKey, int id, UpdateTaskRequest request, int? teamId = null) =>
        Update(_userProfileService.GetOrCreateByKey(userKey).Id, id, request, teamId);

    public TaskResponse? Update(int userId, int id, UpdateTaskRequest request, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        var user = RequireUser(userId);
        RequireScope(userId, teamId);
        var task = ScopedTasks(userId, teamId).SingleOrDefault(task => task.Id == id);

        if (task == null)
        {
            _logger.LogWarning("Task update failed. Task not found. Id: {TaskId}", id);
            return null;
        }

        RequireVersion(task, request.Version);

        var wasDone = IsDone(task.Status);
        var status = ResolveStatus(request.IsCompleted, request.Status);
        var before = System.Text.Json.JsonSerializer.Serialize(task);
        var statusChanged = task.Status != status;
        var now = DateTime.UtcNow;

        var assignee = ValidateAssignee(request.AssigneeUserProfileId, teamId);
        var checklist = request.Checklist != null ? SerializeChecklist(request.Checklist) : task.ChecklistJson;
        task.AssigneeUserProfileId = assignee;
        task.ChecklistJson = checklist;
        task.Title = request.Title;
        task.Description = request.Description;
        task.IsCompleted = IsDone(status);
        task.Status = status;
        task.DueDate = NormalizeDueDate(request.DueDate);
        task.Priority = request.Priority;
        task.Tags = NormalizeTags(request.Tags);
        task.UpdatedAt = now;

        if (!wasDone && IsDone(status))
        {
            _petService.ApplyTaskCompletionReward(task, task.AssigneeUserProfileId ?? user.Id, now);
        }
        else if (wasDone && !IsDone(status))
        {
            _petService.RevokeTaskCompletionReward(task, now);
        }

        _context.SaveChanges();
        if (!wasDone && IsDone(status)) PetCollectionService.RecordSavedMilestones(_context, task.AssigneeUserProfileId ?? user.Id, now);
        var response = ToResponse(task);
        if (statusChanged) response.Undo = SaveUndo(userId, task, before, false);
        scope.Commit();

        _logger.LogInformation("Task updated. Id: {TaskId}", task.Id);

        return response;
    }
    public bool Delete(string? userKey, int id, uint? version = null, int? teamId = null) =>
        Delete(_userProfileService.GetOrCreateByKey(userKey).Id, id, version, teamId);

    public bool Delete(int userId, int id, uint? version = null, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        RequireUser(userId);
        RequireScope(userId, teamId);
        var task = ScopedTasks(userId, teamId).SingleOrDefault(task => task.Id == id);

        if (task == null)
        {
            _logger.LogWarning("Task delete failed. Task not found. Id: {TaskId}", id);
            return false;
        }

        RequireVersion(task, version);

        // Keep earned XP and its receipt when a completed task is deleted. Explicit
        // detachment also models ON DELETE SET NULL in the in-memory test provider.
        var reward = _context.CompletionRewards.SingleOrDefault(item => item.TaskId == task.Id);
        if (reward != null)
        {
            reward.Task = null;
            reward.TaskId = null;
        }
        _context.Tasks.Remove(task);
        _context.SaveChanges();
        scope.Commit();

        _logger.LogInformation("Task deleted. Id: {TaskId}", id);

        return true;
    }

    private TaskResponse ToResponse(TaskItem task, IReadOnlyDictionary<int, string>? creatorNames = null)
    {
        var work = ReadWork(task);
        var creatorName = task.UserProfileId is { } creatorId
            ? creatorNames?.GetValueOrDefault(creatorId) ?? _context.UserProfiles.AsNoTracking()
                .Where(user => user.Id == creatorId).Select(user => user.DisplayName).SingleOrDefault()
            : null;
        return new TaskResponse
        {
            Id = task.Id,
            TeamId = task.TeamId,
            AssigneeUserProfileId = task.AssigneeUserProfileId,
            AssigneeDisplayName = task.AssigneeUserProfileId is { } assigneeId
                ? _context.UserProfiles.AsNoTracking().Where(user => user.Id == assigneeId).Select(user => user.DisplayName).SingleOrDefault()
                : null,
            Checklist = System.Text.Json.JsonSerializer.Deserialize<List<ChecklistItem>>(task.ChecklistJson) ?? [],
            // Only shared indicators; never expose anyone's private savepoint here.
            NeedsHelp = task.TeamId.HasValue && work.Help?.Status == "open",
            HandoffPending = task.TeamId.HasValue && work.Handoff?.Status is "sent" or "question",
            HandoffRecipientId = task.TeamId.HasValue && work.Handoff?.Status is "sent" or "question" ? work.Handoff.ToUserId : null,
            NoteCount = work.Notes.Count,
            CreatedByUserProfileId = task.UserProfileId,
            CreatedByDisplayName = creatorName ?? "退会したユーザー",
            Version = task.Version,
            Title = task.Title,
            Description = task.Description,
            IsCompleted = task.IsCompleted,
            Status = task.Status,
            DueDate = task.DueDate,
            Priority = task.Priority,
            Tags = task.Tags,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    private UserProfile RequireUser(int userId) => _context.UserProfiles.SingleOrDefault(user => user.Id == userId)
        ?? throw new TeamOperationException("account_not_found", "アカウントが見つかりません。再ログインしてください。", 401);

    private void RequireScope(int userId, int? teamId)
    {
        if (teamId.HasValue) new TeamService(_context).RequireMember(userId, teamId.Value);
    }

    private IQueryable<TaskItem> ScopedTasks(int userId, int? teamId) => teamId.HasValue
        ? _context.Tasks.Where(task => task.TeamId == teamId.Value)
        : _context.Tasks.Where(task => task.TeamId == null && task.UserProfileId == userId);

    private static void RequireVersion(TaskItem task, uint? version)
    {
        if ((task.TeamId.HasValue && !version.HasValue) || (version.HasValue && version.Value != task.Version))
            throw new DbUpdateConcurrencyException("別の操作で更新されたか、更新バージョンが指定されていません。");
    }

    private static TaskItemStatus ResolveStatus(bool isCompleted, TaskItemStatus status)
    {
        return isCompleted ? TaskItemStatus.Done : status;
    }

    private static bool IsDone(TaskItemStatus status)
    {
        return status == TaskItemStatus.Done;
    }

    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        var normalizedTags = tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readableTags = string.Join(", ", normalizedTags);

        return readableTags.Length <= 300
            ? readableTags
            : string.Join(",", normalizedTags);
    }

    private static DateTime? NormalizeDueDate(DateTime? dueDate)
    {
        if (!dueDate.HasValue)
        {
            return null;
        }

        if (dueDate.Value.Kind == DateTimeKind.Utc)
        {
            return dueDate.Value;
        }

        if (dueDate.Value.Kind == DateTimeKind.Unspecified)
        {
            return DateTime.SpecifyKind(dueDate.Value, DateTimeKind.Utc);
        }

        return dueDate.Value.ToUniversalTime();
    }

}

public sealed class TaskLimitExceededException(int limit, bool team = false)
    : Exception($"{(team ? "1チーム" : "1ユーザー")}あたりのタスク上限は{limit}件です。不要なタスクを削除してから再度お試しください。")
{
}
