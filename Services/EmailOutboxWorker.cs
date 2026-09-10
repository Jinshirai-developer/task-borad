using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;

namespace TaskApi.Services;

public sealed class EmailOutboxDispatcher(
    AppDbContext database, IDataProtectionProvider protection,
    ITransactionalEmailSender sender, ILogger<EmailOutboxDispatcher> logger)
{
    public async Task<bool> DispatchOneAsync(CancellationToken cancellationToken)
    {
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        var now = DateTime.UtcNow;
        // A row lock prevents two application instances from sending the same queued message concurrently.
        var entry = database.Database.IsRelational()
            ? await database.EmailOutbox.FromSqlInterpolated($"SELECT * FROM email_outbox WHERE \"NextAttemptAt\" <= {now} OR \"ExpiresAt\" <= {now} ORDER BY \"NextAttemptAt\" LIMIT 1 FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(cancellationToken)
            : await database.EmailOutbox.Where(item => item.NextAttemptAt <= now || item.ExpiresAt <= now)
                .OrderBy(item => item.NextAttemptAt).FirstOrDefaultAsync(cancellationToken);
        if (entry == null) return false;
        if (entry.ExpiresAt <= now)
        {
            database.EmailOutbox.Remove(entry);
        }
        else
        {
            try
            {
                var json = protection.CreateProtector(EmailOutboxService.ProtectionPurpose).Unprotect(entry.ProtectedPayload);
                var email = JsonSerializer.Deserialize<TransactionalEmail>(json)
                    ?? throw new InvalidOperationException("Invalid queued email payload.");
                await sender.SendAsync(email, cancellationToken);
                database.EmailOutbox.Remove(entry);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                entry.Attempts++;
                entry.NextAttemptAt = entry.Attempts >= 5 ? entry.ExpiresAt
                    : now.AddSeconds(Math.Min(300, 15 * Math.Pow(2, entry.Attempts)));
                // SMTP exceptions can contain recipient addresses. Do not log their messages or payloads.
                logger.LogWarning("Email delivery failed. QueueId: {QueueId}, Attempt: {Attempt}, ErrorType: {ErrorType}",
                    entry.Id, entry.Attempts, error.GetType().Name);
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchOneAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Email queue unavailable. ErrorType: {ErrorType}", error.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
