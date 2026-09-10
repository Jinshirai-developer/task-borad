using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskApi.Configuration;
using TaskApi.Data;

namespace TaskApi.Services;

public sealed class BillingReconciliationWorker(IServiceScopeFactory scopes, IOptions<BillingOptions> options, ILogger<BillingReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!options.Value.IsConfigured) continue;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var before = DateTime.UtcNow.AddMinutes(-2);
                var accounts = await db.TeamBillings.AsNoTracking().Where(item => item.SessionId != null
                    && item.Status != "canceled" && item.Status != "incomplete_expired" && item.Status != "expired"
                    && (item.LastSyncedAt == null || item.LastSyncedAt < before)).OrderBy(item => item.LastSyncedAt).Take(20).Select(item => item.UserProfileId).ToListAsync(stoppingToken);
                foreach (var account in accounts)
                {
                    try { await scope.ServiceProvider.GetRequiredService<TeamBillingService>().ReconcileAsync(account, cancellationToken: stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch { logger.LogWarning("Test billing reconciliation deferred. AccountId: {AccountId}", account); }
                }
                using var write = WorkspaceWriteScope.Begin(db);
                db.BillingEventReceipts.RemoveRange(db.BillingEventReceipts.Where(item => item.ProcessedAt < DateTime.UtcNow.AddDays(-30)).Take(1000));
                db.SaveChanges(); write.Commit();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Test billing reconciliation unavailable; retrying later."); }
        }
    }
}
