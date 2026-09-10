using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public sealed class TaskTagService(AppDbContext context)
{
    public const int MaximumRegisteredTags = 50;

    public TaskTagCatalogResponse GetAll(int userId, int? teamId = null)
    {
        RequireScope(userId, teamId);
        var definitions = Definitions(userId, teamId).AsNoTracking()
            .OrderBy(tag => tag.CreatedAt).ThenBy(tag => tag.Id).ToList();
        var items = new Dictionary<string, TaskTagCountResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in definitions)
            items.TryAdd(tag.Name, new TaskTagCountResponse { Name = tag.Name });

        // One workspace is capped at 500 tasks. Parse its existing CSV in managed
        // code so old tokens and Unicode case comparisons match on every provider.
        var tasks = Tasks(userId, teamId).AsNoTracking().OrderBy(task => task.Id)
            .Select(task => new { task.Tags, task.Status }).ToList();
        var untaggedTasks = 0;
        foreach (var task in tasks)
        {
            var names = Tokenize(task.Tags);
            if (names.Count == 0) untaggedTasks++;
            foreach (var name in names)
            {
                if (!items.TryGetValue(name, out var item))
                {
                    item = new TaskTagCountResponse { Name = name };
                    items.Add(name, item);
                }
                item.Total++;
                switch (task.Status)
                {
                    case TaskItemStatus.Todo: item.Todo++; break;
                    case TaskItemStatus.Doing: item.Doing++; break;
                    case TaskItemStatus.Done: item.Done++; break;
                }
            }
        }
        return new TaskTagCatalogResponse(items.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            tasks.Count, untaggedTasks, definitions.Count, MaximumRegisteredTags);
    }

    public TaskTagCreatedResponse Create(int userId, CreateTaskTagRequest request, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        RequireScope(userId, teamId);
        var rawName = request.Name ?? string.Empty;
        var name = rawName.Trim();
        if (name.Length is < 1 or > 50 || rawName.Contains(',') || rawName.Any(char.IsControl))
            throw new TeamOperationException("invalid_tag_name", "タグ名は1〜50文字で入力してください。カンマや制御文字は使用できません。", 400);
        var normalizedName = name.ToUpperInvariant();
        var definitions = Definitions(userId, teamId);
        if (definitions.Any(tag => tag.NormalizedName == normalizedName))
            throw new TeamOperationException("tag_already_exists", "この分類タグはすでに登録されています。");
        if (definitions.Count() >= MaximumRegisteredTags)
            throw new TeamOperationException("tag_limit", $"登録できる分類タグは{MaximumRegisteredTags}件までです。");
        context.TaskTagDefinitions.Add(new TaskTagDefinition
        {
            UserProfileId = teamId.HasValue ? null : userId,
            TeamId = teamId,
            Name = name,
            NormalizedName = normalizedName
        });
        context.SaveChanges();
        scope.Commit();
        return new TaskTagCreatedResponse(name);
    }

    public static List<string> Tokenize(string? tags) => string.IsNullOrWhiteSpace(tags) ? []
        : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public TaskTagCatalogResponse Manage(int userId, ManageTaskTagRequest request, int? teamId = null)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        RequireScope(userId, teamId);
        var name = request.Name.Trim();
        var target = request.TargetName?.Trim();
        var catalog = GetAll(userId, teamId);
        if (!catalog.Items.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new TeamOperationException("tag_not_found", "タグは別の操作で変更されています。一覧を更新してください。", 409);
        if (request.Action is not ("rename" or "delete" or "merge"))
            throw new TeamOperationException("invalid_tag_action", "タグの操作を選んでください。", 400);
        if (request.Action != "delete")
        {
            if (string.IsNullOrWhiteSpace(target) || target.Length > 50 || target.Contains(',') || target.Any(char.IsControl))
                throw new TeamOperationException("invalid_tag_name", "新しいタグ名は1〜50文字で入力してください。カンマや制御文字は使用できません。", 400);
            var targetExists = catalog.Items.Any(item => string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
            if (request.Action == "merge" && (!targetExists || string.Equals(name, target, StringComparison.OrdinalIgnoreCase)))
                throw new TeamOperationException("invalid_merge_target", "統合先には別の既存タグを選んでください。", 400);
            if (request.Action == "rename" && targetExists && !string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                throw new TeamOperationException("tag_already_exists", "同名のタグがあります。「統合」を使ってください。", 409);
        }
        var tasks = Tasks(userId, teamId).ToList();
        var updates = new List<(TaskItem Task, string Tags)>();
        foreach (var task in tasks)
        {
            var tokens = Tokenize(task.Tags);
            if (!tokens.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var next = tokens.Select(token => string.Equals(token, name, StringComparison.OrdinalIgnoreCase)
                    ? request.Action == "delete" ? null : target : token)
                .Where(token => token != null).Distinct(StringComparer.OrdinalIgnoreCase);
            var csv = string.Join(", ", next);
            if (csv.Length > 300)
                throw new TeamOperationException("tag_length_limit", "変更後にタグが300文字を超えるタスクがあります。短い名前にしてください。", 400);
            updates.Add((task, csv));
        }
        var definitions = Definitions(userId, teamId).ToList();
        var source = definitions.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (request.Action == "rename")
        {
            if (source != null) { source.Name = target!; source.NormalizedName = target!.ToUpperInvariant(); }
            else if (definitions.Count < MaximumRegisteredTags)
                context.TaskTagDefinitions.Add(new TaskTagDefinition { UserProfileId = teamId == null ? userId : null,
                    TeamId = teamId, Name = target!, NormalizedName = target!.ToUpperInvariant() });
        }
        else if (source != null) context.TaskTagDefinitions.Remove(source);
        foreach (var (task, tags) in updates)
        {
            task.Tags = tags.Length == 0 ? null : tags;
            task.UpdatedAt = DateTime.UtcNow;
        }
        context.SaveChanges();
        var result = GetAll(userId, teamId);
        scope.Commit();
        return result;
    }

    private void RequireScope(int userId, int? teamId)
    {
        if (!context.UserProfiles.Any(user => user.Id == userId))
            throw new TeamOperationException("authentication_required", "もう一度ログインしてください。", 401);
        if (teamId.HasValue) new TeamService(context).RequireMember(userId, teamId.Value);
    }

    private IQueryable<TaskTagDefinition> Definitions(int userId, int? teamId) => teamId.HasValue
        ? context.TaskTagDefinitions.Where(tag => tag.TeamId == teamId.Value && tag.UserProfileId == null)
        : context.TaskTagDefinitions.Where(tag => tag.UserProfileId == userId && tag.TeamId == null);

    private IQueryable<TaskItem> Tasks(int userId, int? teamId) => teamId.HasValue
        ? context.Tasks.Where(task => task.TeamId == teamId.Value)
        : context.Tasks.Where(task => task.UserProfileId == userId && task.TeamId == null);
}
