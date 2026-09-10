using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;
using TaskApi.Models;

namespace TaskApi.Services;

public class PetService
{
    private const int CompletionRewardExperience = 25;
    private const int MaxEnergy = 100;

    private readonly AppDbContext _context;
    private readonly ILogger<PetService> _logger;
    private readonly UserProfileService _userProfileService;

    public PetService(AppDbContext context, ILogger<PetService> logger, UserProfileService userProfileService)
    {
        _context = context;
        _logger = logger;
        _userProfileService = userProfileService;
    }

    public PetResponse GetProfile(string? userKey)
    {
        var pet = GetOrCreateProfile(userKey);
        RefreshMood(pet);
        _context.SaveChanges();

        return ToResponse(pet);
    }

    public PetResponse GetProfile(int userId)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        var response = GetProfile(RequireUserKey(userId));
        scope.Commit();
        return response;
    }

    public PetResponse UpdateProfile(int userId, UpdatePetRequest request)
    {
        using var scope = WorkspaceWriteScope.Begin(_context);
        var response = UpdateProfile(RequireUserKey(userId), request);
        scope.Commit();
        return response;
    }

    private string RequireUserKey(int userId) => _context.UserProfiles
        .Where(user => user.Id == userId).Select(user => user.UserKey).SingleOrDefault()
        ?? throw new TeamOperationException("authentication_required", "もう一度ログインしてください。", 401);

    public PetResponse UpdateProfile(string? userKey, UpdatePetRequest request)
    {
        var pet = GetOrCreateProfile(userKey);
        if (request.Species != null)
            ProgressionCatalog.RequirePet(request.Species, pet.Level);
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Task Pet";
        }

        pet.Name = name;
        if (request.Species != null)
        {
            pet.Species = request.Species;
        }
        pet.UpdatedAt = DateTime.UtcNow;

        RefreshMood(pet);
        _context.SaveChanges();

        _logger.LogInformation("Pet profile updated. Id: {PetId}", pet.Id);

        return ToResponse(pet);
    }

    public PetResponse RewardForCompletedTask(string? userKey)
    {
        var pet = ApplyCompletionReward(userKey);
        _context.SaveChanges();

        return ToResponse(pet);
    }

    public PetProfile ApplyCompletionReward(string? userKey)
    {
        var pet = GetOrCreateProfile(userKey);
        var now = DateTime.UtcNow;
        ApplyReward(pet, now);
        // Compatibility callers have no task receipt. Treat this activity as the
        // untracked baseline instead of losing it when tracked rewards are revoked.
        pet.LegacyLastCompletedAt = pet.LastCompletedAt;
        pet.LegacyStreakDays = pet.StreakDays;
        return pet;
    }

    // These methods only stage changes. TaskService saves the task, receipt and pet
    // atomically inside its workspace admission lock and database transaction.
    public void ApplyTaskCompletionReward(TaskItem task, int recipientUserId, DateTime now)
    {
        var reward = FindTaskReward(task);
        if (reward is { RevokedAt: null })
        {
            task.CompletionRewardedAt = reward.AwardedAt;
            return;
        }

        var user = _context.UserProfiles.SingleOrDefault(profile => profile.Id == recipientUserId)
            ?? throw new TeamOperationException("authentication_required", "もう一度ログインしてください。", 401);
        var pet = GetOrCreateProfile(user.UserKey);
        var previousEnergy = pet.Energy;
        ApplyReward(pet, now);

        if (reward == null)
        {
            reward = new CompletionReward { Task = task };
            _context.CompletionRewards.Add(reward);
        }
        reward.UserProfileId = recipientUserId;
        reward.AwardedAt = now;
        reward.RevokedAt = null;
        reward.Experience = CompletionRewardExperience;
        reward.EnergyGranted = pet.Energy - previousEnergy;
        task.CompletionRewardedAt = now;
        RecalculateActivity(pet);
    }

    public void RevokeTaskCompletionReward(TaskItem task, DateTime now)
    {
        var reward = FindTaskReward(task);
        task.CompletionRewardedAt = null;
        if (reward == null || reward.RevokedAt.HasValue) return;

        reward.RevokedAt = now < reward.AwardedAt ? reward.AwardedAt : now;
        // A removed or unknown legacy receiver is never inferred from the person
        // reopening the task, and must never cause account or pet recreation.
        if (reward.UserProfileId is not { } receiverId
            || !_context.UserProfiles.Any(user => user.Id == receiverId)) return;
        var pet = _context.PetProfiles.SingleOrDefault(profile => profile.UserProfileId == receiverId);
        if (pet == null) return;

        pet.TotalExperience = Math.Max(0, pet.TotalExperience - reward.Experience);
        pet.CompletedTaskCount = Math.Max(0, pet.CompletedTaskCount - 1);
        pet.Energy = Math.Max(0, pet.Energy - reward.EnergyGranted);
        RecalculateLevel(pet);
        RecalculateActivity(pet);
        pet.Mood = pet.LastCompletedAt.HasValue ? "Happy" : "Idle";
        RefreshMood(pet);
        pet.UpdatedAt = now;
        ProgressionCatalog.EnforceSelections(_context, pet);
        PetCollectionService.EnforceEquipment(_context, pet);
        _logger.LogInformation("Task completion reward revoked. TaskId: {TaskId}, ReceiverId: {ReceiverId}", task.Id, receiverId);
    }

    private CompletionReward? FindTaskReward(TaskItem task)
    {
        return _context.CompletionRewards.Local.FirstOrDefault(reward => ReferenceEquals(reward.Task, task)
                || (task.Id > 0 && reward.TaskId == task.Id))
            ?? (task.Id > 0 ? _context.CompletionRewards.SingleOrDefault(reward => reward.TaskId == task.Id) : null);
    }

    public void RestoreRevokedTaskCompletionReward(TaskItem task, DateTime? originalAwardedAt, DateTime now)
    {
        var reward = FindTaskReward(task);
        task.CompletionRewardedAt = originalAwardedAt;
        if (reward == null || reward.RevokedAt == null) return;
        reward.RevokedAt = null;
        if (reward.UserProfileId is not { } receiverId) return;
        var pet = _context.PetProfiles.SingleOrDefault(item => item.UserProfileId == receiverId);
        if (pet == null) return;
        pet.TotalExperience += reward.Experience;
        pet.CompletedTaskCount++;
        var previousEnergy = pet.Energy;
        pet.Energy = Math.Min(MaxEnergy, pet.Energy + reward.EnergyGranted);
        reward.EnergyGranted = pet.Energy - previousEnergy;
        RecalculateLevel(pet);
        RecalculateActivity(pet);
        pet.UpdatedAt = now;
        RefreshMood(pet);
    }

    private void RecalculateActivity(PetProfile pet)
    {
        // A query alone misses added/reactivated rows and may include a row whose
        // pending RevokedAt changed in this unit of work, so merge tracked state.
        var rewards = _context.CompletionRewards
            .Where(reward => reward.UserProfileId == pet.UserProfileId && reward.RevokedAt == null)
            .ToList().Concat(_context.CompletionRewards.Local).Distinct()
            .Where(reward => reward.UserProfileId == pet.UserProfileId && reward.RevokedAt == null
                && _context.Entry(reward).State != EntityState.Deleted).ToList();
        var latest = rewards.Select(reward => (DateTime?)reward.AwardedAt).DefaultIfEmpty().Max();
        if (pet.LegacyLastCompletedAt is { } baseline && (!latest.HasValue || baseline > latest.Value))
            latest = baseline;
        pet.LastCompletedAt = latest;
        pet.StreakDays = 0;
        if (!latest.HasValue) return;

        var dates = rewards.Select(reward => reward.AwardedAt.Date).ToHashSet();
        var baselineEnd = pet.LegacyLastCompletedAt?.Date;
        var baselineStart = baselineEnd?.AddDays(-Math.Max(0, pet.LegacyStreakDays - 1));
        var day = latest.Value.Date;
        while (true)
        {
            if (dates.Contains(day))
            {
                pet.StreakDays++;
                day = day.AddDays(-1);
            }
            else if (pet.LegacyStreakDays > 0 && baselineStart.HasValue && baselineEnd.HasValue
                && day >= baselineStart.Value && day <= baselineEnd.Value)
            {
                pet.StreakDays += (int)(day - baselineStart.Value).TotalDays + 1;
                day = baselineStart.Value.AddDays(-1);
            }
            else break;
        }
    }

    private static void RecalculateLevel(PetProfile pet)
    {
        pet.Level = 1;
        pet.Experience = pet.TotalExperience;
        while (pet.Experience >= GetExperienceToNextLevel(pet.Level))
        {
            pet.Experience -= GetExperienceToNextLevel(pet.Level);
            pet.Level++;
        }
    }

    private void ApplyReward(PetProfile pet, DateTime now)
    {
        pet.CompletedTaskCount++;
        pet.TotalExperience += CompletionRewardExperience;
        pet.Experience += CompletionRewardExperience;
        pet.Energy = Math.Min(MaxEnergy, pet.Energy + 12);
        UpdateStreak(pet, now);

        var leveledUp = ApplyLevelUp(pet);
        PetCollectionService.RecordMilestones(_context, pet, now);
        pet.Mood = leveledUp ? "Proud" : "Happy";
        pet.LastCompletedAt = now;
        pet.UpdatedAt = now;

        _logger.LogInformation(
            "Pet rewarded. Level: {Level}, TotalExperience: {TotalExperience}",
            pet.Level,
            pet.TotalExperience);
    }

    private PetProfile GetOrCreateProfile(string? userKey)
    {
        var user = _userProfileService.GetOrCreateByKey(userKey);
        var pet = _context.PetProfiles.SingleOrDefault(profile => profile.UserProfileId == user.Id);

        if (pet != null)
        {
            return pet;
        }

        pet = new PetProfile
        {
            UserProfileId = user.Id,
            Name = "Task Pet",
            Level = 1,
            Energy = 80,
            Mood = "Idle",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.PetProfiles.Add(pet);

        return pet;
    }

    private static void UpdateStreak(PetProfile pet, DateTime now)
    {
        var today = now.Date;

        if (pet.LastCompletedAt == null)
        {
            pet.StreakDays = 1;
            return;
        }

        var lastCompletedDate = pet.LastCompletedAt.Value.Date;

        if (lastCompletedDate == today)
        {
            pet.StreakDays = Math.Max(1, pet.StreakDays);
            return;
        }

        if (lastCompletedDate == today.AddDays(-1))
        {
            pet.StreakDays++;
            return;
        }

        pet.StreakDays = 1;
    }

    private static bool ApplyLevelUp(PetProfile pet)
    {
        var leveledUp = false;

        while (pet.Experience >= GetExperienceToNextLevel(pet.Level))
        {
            pet.Experience -= GetExperienceToNextLevel(pet.Level);
            pet.Level++;
            pet.Energy = MaxEnergy;
            leveledUp = true;
        }

        return leveledUp;
    }

    private static void RefreshMood(PetProfile pet)
    {
        if (pet.LastCompletedAt == null)
        {
            pet.Mood = "Idle";
            return;
        }

        var inactiveDays = (DateTime.UtcNow.Date - pet.LastCompletedAt.Value.Date).Days;

        if (pet.StreakDays >= 3)
        {
            pet.Mood = "Proud";
            return;
        }

        pet.Mood = inactiveDays > 0 ? "Idle" : "Happy";
    }

    private static int GetExperienceToNextLevel(int level)
    {
        return 100 + ((level - 1) * 50);
    }

    private static PetResponse ToResponse(PetProfile pet)
    {
        var experienceToNextLevel = GetExperienceToNextLevel(pet.Level);
        var effectiveEnergy = GetEffectiveEnergy(pet);

        return new PetResponse
        {
            Id = pet.Id,
            Name = pet.Name,
            Species = pet.Species,
            Title = GetTitle(pet),
            Level = pet.Level,
            Experience = pet.Experience,
            ExperienceToNextLevel = experienceToNextLevel,
            ExperienceProgress = GetExperienceProgress(pet.Experience, experienceToNextLevel),
            ExperienceRemaining = Math.Max(0, experienceToNextLevel - pet.Experience),
            TotalExperience = pet.TotalExperience,
            CompletedTaskCount = pet.CompletedTaskCount,
            StreakDays = pet.StreakDays,
            Energy = effectiveEnergy,
            Mood = pet.Mood,
            MoodLabel = GetMoodLabel(pet.Mood),
            EnergyLabel = GetEnergyLabel(effectiveEnergy),
            Message = GetMoodMessage(pet),
            Achievements = GetAchievements(pet),
            LastCompletedAt = pet.LastCompletedAt,
            UpdatedAt = pet.UpdatedAt
        };
    }

    private static int GetExperienceProgress(int experience, int experienceToNextLevel)
    {
        return Math.Min(100, (int)Math.Round((double)experience / experienceToNextLevel * 100));
    }

    private static int GetEffectiveEnergy(PetProfile pet)
    {
        // Holidays and long-running work are not failures. Keep stored energy for
        // receipt/undo compatibility, without a time-based penalty.
        return Math.Clamp(pet.Energy, 0, MaxEnergy);
    }

    private static string GetTitle(PetProfile pet)
    {
        if (pet.StreakDays >= 7)
        {
            return "継続の達人";
        }

        if (pet.Level >= 10)
        {
            return "熟練タスクメイト";
        }

        if (pet.Level >= 5)
        {
            return "頼れる相棒";
        }

        if (pet.CompletedTaskCount >= 10)
        {
            return "がんばり屋";
        }

        return "見習い相棒";
    }

    private static string GetMoodLabel(string mood)
    {
        return mood switch
        {
            "Happy" => "ごきげん",
            "Proud" => "誇らしげ",
            "Sleepy" => "うとうと",
            "Sad" => "しょんぼり",
            _ => "待機中"
        };
    }

    private static string GetEnergyLabel(int energy)
    {
        if (energy >= 80)
        {
            return "元気いっぱい";
        }

        if (energy >= 50)
        {
            return "ふつう";
        }

        if (energy >= 25)
        {
            return "少し疲れ気味";
        }

        return "元気不足";
    }

    private static List<string> GetAchievements(PetProfile pet)
    {
        var achievements = new List<string>();

        if (pet.CompletedTaskCount >= 1)
        {
            achievements.Add("初完了");
        }

        if (pet.CompletedTaskCount >= 5)
        {
            achievements.Add("5タスク達成");
        }

        if (pet.CompletedTaskCount >= 10)
        {
            achievements.Add("10タスク達成");
        }

        if (pet.Level >= 2)
        {
            achievements.Add("レベル2到達");
        }

        if (pet.Level >= 5)
        {
            achievements.Add("レベル5到達");
        }

        if (pet.StreakDays >= 3)
        {
            achievements.Add("3日連続");
        }

        if (pet.StreakDays >= 7)
        {
            achievements.Add("7日連続");
        }

        if (achievements.Count == 0)
        {
            achievements.Add("これから開始");
        }

        return achievements;
    }

    private static string GetMoodMessage(PetProfile pet)
    {
        return pet.Mood switch
        {
            "Happy" => "経験値を獲得しました",
            "Proud" => "連続達成中です",
            "Sleepy" => "少し休み気味です",
            "Sad" => "タスクを進めて元気を戻しましょう",
            _ => "今日も一歩ずつ進めましょう"
        };
    }
}
