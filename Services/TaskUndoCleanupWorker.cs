using Microsoft.EntityFrameworkCore;
using TaskApi.Data;

namespace TaskApi.Services;

public sealed class TaskUndoCleanupWorker(IServiceScopeFactory scopes, ILogger<TaskUndoCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var services = scopes.CreateScope();
                    var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
                    using var write = WorkspaceWriteScope.Begin(context);
                    var now = DateTime.UtcNow;
                    context.TaskUndoEntries.RemoveRange(context.TaskUndoEntries.Where(item => item.ExpiresAt <= now));
                    context.SaveChanges();
                    write.Commit();
                }
                catch (Exception error) { logger.LogWarning(error, "Expired task undo cleanup failed; will retry."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
