using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.DTOs;

namespace TaskApi.Services;

public sealed class WeeklyReviewService(AppDbContext context)
{
    public WeeklyReviewResponse Get(int userId, DateTime? utcNow = null)
    {
        if (!context.UserProfiles.Any(user => user.Id == userId))
            throw new TeamOperationException("authentication_required", "もう一度ログインしてください。", 401);
        var now = utcNow ?? DateTime.UtcNow;
        var today = now.AddHours(9).Date;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var start = DateTime.SpecifyKind(monday.AddHours(-9), DateTimeKind.Utc);
        var previous = start.AddDays(-7);
        var awards = context.CompletionRewards.AsNoTracking().Where(item => item.UserProfileId == userId
            && item.RevokedAt == null && item.AwardedAt >= previous && item.AwardedAt <= now)
            .Select(item => item.AwardedAt).ToList();
        var thisWeek = awards.Count(date => date >= start);
        var lastWeek = awards.Count(date => date < start);
        var days = Enumerable.Range(0, 7).Select(offset => new WeeklyDay(
            monday.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            awards.Count(date => date.AddHours(9).Date == monday.AddDays(offset)))).ToList();
        var message = thisWeek == 0 ? "今週も、自分のペースで。一つできたら一緒に喜ぼう。"
            : $"今週は{thisWeek}件できたね！ひとつずつ進めたこと、ちゃんと覚えているよ。";
        return new(monday.ToString("yyyy-MM-dd"), monday.AddDays(6).ToString("yyyy-MM-dd"),
            thisWeek, lastWeek, thisWeek - lastWeek, days, message);
    }
}
