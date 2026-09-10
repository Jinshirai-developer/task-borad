using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

// Profile and task/pet ownership only. Authentication is managed by ASP.NET Core Identity.
public class UserProfileService(AppDbContext context, ILogger<UserProfileService> logger)
{
    public UserProfile GetOrCreateByKey(string? userKey)
    {
        var key = NormalizeUserKey(userKey);
        var user = context.UserProfiles.SingleOrDefault(profile => profile.UserKey == key);
        if (user != null) return user;
        user = new UserProfile
        {
            UserKey = key,
            UserName = key,
            NormalizedUserName = key.ToUpperInvariant(),
            DisplayName = key == "guest" ? "Guest" : key,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        context.UserProfiles.Add(user);
        context.SaveChanges();
        return user;
    }

    public UserProfileResponse GetProfile(string? userKey) => ToResponse(GetOrCreateByKey(userKey));

    public UserProfileResponse GetProfile(int userId) => ToResponse(RequireExisting(userId));

    public UserProfileResponse UpdateProfile(int userId, UpdateUserProfileRequest request)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var user = RequireExisting(userId);
        var response = UpdateProfile(user.UserKey, request);
        scope.Commit();
        return response;
    }

    public UserPreferencesResponse GetPreferences(int userId)
    {
        var user = RequireExisting(userId);
        return new() { Theme = user.Theme, Layout = user.Layout };
    }

    public UserPreferencesResponse UpdatePreferences(int userId, UpdateUserPreferencesRequest request)
    {
        if (request.Theme is not ("classic" or "retro" or "light" or "dark" or "forest" or "sunset")
            || request.Layout is not ("board" or "list" or "compact" or "gallery" or "focus"))
            throw new ArgumentException("対応していない表示設定です。");
        using var scope = WorkspaceWriteScope.Begin(context);
        var user = RequireExisting(userId);
        var level = context.PetProfiles.Where(pet => pet.UserProfileId == userId).Select(pet => (int?)pet.Level).SingleOrDefault() ?? 1;
        ProgressionCatalog.RequirePreferences(request.Theme, request.Layout, level);
        user.Theme = request.Theme;
        user.Layout = request.Layout;
        user.UpdatedAt = DateTime.UtcNow;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        context.SaveChanges();
        scope.Commit();
        return new() { Theme = user.Theme, Layout = user.Layout };
    }

    public UnlockCatalogResponse GetUnlocks(int userId)
    {
        RequireExisting(userId);
        var pet = context.PetProfiles.AsNoTracking().SingleOrDefault(item => item.UserProfileId == userId);
        return ProgressionCatalog.CreateResponse(pet?.Level ?? 1, pet?.TotalExperience ?? 0);
    }

    private UserProfile RequireExisting(int userId) =>
        context.UserProfiles.SingleOrDefault(profile => profile.Id == userId)
        ?? throw new TeamOperationException("authentication_required", "もう一度ログインしてください。", 401);

    public UserProfileResponse UpdateProfile(string? userKey, UpdateUserProfileRequest request)
    {
        var user = GetOrCreateByKey(userKey);
        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? user.UserKey : request.DisplayName.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        context.SaveChanges();
        return ToResponse(user);
    }

    public bool DeleteProfile(int userId)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var user = context.UserProfiles.SingleOrDefault(profile => profile.Id == userId);
        if (user == null) return false;
        var deleted = DeleteProfile(user.UserKey);
        scope.Commit();
        return deleted;
    }

    public bool DeleteProfile(string? userKey)
    {
        using var scope = WorkspaceWriteScope.Begin(context);
        var key = NormalizeUserKey(userKey);
        var user = context.UserProfiles.SingleOrDefault(profile => profile.UserKey == key);
        if (user == null) return false;
        if (context.Teams.Any(team => team.OwnerUserProfileId == user.Id))
            throw new TeamOperationException("team_owner", "チームの管理者です。先に管理者を他のメンバーへ引き継ぐか、チームを削除してください。");
        var billing = context.TeamBillings.SingleOrDefault(item => item.UserProfileId == user.Id);
        if (billing?.HasContract == true || billing?.OperationUntil > DateTime.UtcNow)
            throw new TeamOperationException("billing_contract_active", "アカウントの決済・契約が残っています。設定 → アカウント → プラン・契約管理で決済を中止するか契約を終了してから退会してください。");
        context.TeamBillings.RemoveRange(context.TeamBillings.Where(item => item.UserProfileId == user.Id));
        context.BillingEventReceipts.RemoveRange(context.BillingEventReceipts.Where(item => item.UserProfileId == user.Id));
        TaskService.RemoveCompanionUser(context, user.Id, deletingAccount: true);
        var personalTaskIds = context.Tasks.Where(task => task.UserProfileId == user.Id && task.TeamId == null)
            .Select(task => task.Id).ToList();
        context.CompletionRewards.RemoveRange(context.CompletionRewards.Where(reward =>
            (reward.TaskId.HasValue && personalTaskIds.Contains(reward.TaskId.Value))
            || (reward.TaskId == null && reward.UserProfileId == user.Id)));
        foreach (var reward in context.CompletionRewards.Where(reward => reward.UserProfileId == user.Id))
            reward.UserProfileId = null;
        context.Tasks.RemoveRange(context.Tasks.Where(task => task.UserProfileId == user.Id && task.TeamId == null));
        foreach (var task in context.Tasks.Where(task => task.AssigneeUserProfileId == user.Id))
        {
            task.AssigneeUserProfileId = null;
            task.UpdatedAt = DateTime.UtcNow;
        }
        context.TaskUndoEntries.RemoveRange(context.TaskUndoEntries.Where(item => item.UserProfileId == user.Id));
        foreach (var task in context.Tasks.Where(task => task.UserProfileId == user.Id && task.TeamId != null))
            task.UserProfileId = null;
        context.TaskTagDefinitions.RemoveRange(context.TaskTagDefinitions.Where(tag => tag.UserProfileId == user.Id));
        context.TeamMembers.RemoveRange(context.TeamMembers.Where(member => member.UserProfileId == user.Id));
        context.PetProfiles.RemoveRange(context.PetProfiles.Where(pet => pet.UserProfileId == user.Id));
        context.UserProfiles.Remove(user);
        context.SaveChanges();
        scope.Commit();
        logger.LogInformation("User profile deleted. UserId: {UserId}", user.Id);
        return true;
    }

    public static string NormalizeUserKey(string? userKey) =>
        string.IsNullOrWhiteSpace(userKey) ? "guest" : userKey.Trim().ToLowerInvariant();

    public static UserProfileResponse ToResponse(UserProfile user) => new()
    {
        Id = user.Id, UserKey = user.UserKey, DisplayName = user.DisplayName
    };
}
