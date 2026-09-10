using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskApi.Data;
using TaskApi.Models;
using AuthOptions = TaskApi.Configuration.AuthenticationOptions;

namespace TaskApi.Services;

public sealed record TransactionalEmail(string Address, string Subject, string Text);

public sealed class EmailOutboxService(
    AppDbContext database, IDataProtectionProvider protection, IOptions<AuthOptions> options)
{
    public const string ProtectionPurpose = "TaskBoard.EmailOutbox.v1";

    public async Task QueueAsync(UserProfile user, string purpose, string token, TimeSpan lifetime)
    {
        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}{options.Value.PublicEntryPath}auth.html?mode={purpose}#userId={user.Id}&token={token}";
        var confirmation = purpose == "confirm";
        var message = new TransactionalEmail(user.Email!,
            confirmation ? "[Task Board] メールアドレスの確認" : "[Task Board] パスワードの再設定",
            $"Task Board の{(confirmation ? "メール確認" : "パスワード再設定")}を受け付けました。\n\n{link}\n\n"
            + $"リンクの有効期限は{(confirmation ? "24時間" : "30分")}です。心当たりがなければ操作せず、このメールを削除してください。\n"
            + "このリンクを他の人に共有しないでください。");
        var payload = protection.CreateProtector(ProtectionPurpose).Protect(JsonSerializer.Serialize(message));
        var expiresAt = DateTime.UtcNow.Add(lifetime);
        var nextAttemptAt = DateTime.UtcNow;
        if (database.Database.IsRelational())
        {
            // Atomic upsert remains safe if a worker deletes the previous message during a resend.
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO email_outbox ("UserProfileId", "Purpose", "ProtectedPayload", "Attempts", "NextAttemptAt", "ExpiresAt")
                VALUES ({user.Id}, {purpose}, {payload}, 0, {nextAttemptAt}, {expiresAt})
                ON CONFLICT ("UserProfileId", "Purpose") DO UPDATE
                SET "ProtectedPayload" = EXCLUDED."ProtectedPayload", "Attempts" = 0,
                    "NextAttemptAt" = EXCLUDED."NextAttemptAt", "ExpiresAt" = EXCLUDED."ExpiresAt";
                """);
            return;
        }
        var existing = await database.EmailOutbox.SingleOrDefaultAsync(item =>
            item.UserProfileId == user.Id && item.Purpose == purpose);
        var entry = existing ?? new EmailOutboxMessage { UserProfileId = user.Id, Purpose = purpose };
        entry.ProtectedPayload = payload;
        entry.ExpiresAt = expiresAt;
        entry.NextAttemptAt = nextAttemptAt;
        entry.Attempts = 0;
        if (existing == null) database.EmailOutbox.Add(entry);
        await database.SaveChangesAsync();
    }

    public async Task RemoveForUserAsync(int userId)
    {
        if (database.Database.IsRelational())
        {
            await database.EmailOutbox.Where(item => item.UserProfileId == userId).ExecuteDeleteAsync();
            return;
        }
        database.EmailOutbox.RemoveRange(await database.EmailOutbox.Where(item => item.UserProfileId == userId).ToListAsync());
        await database.SaveChangesAsync();
    }
}
