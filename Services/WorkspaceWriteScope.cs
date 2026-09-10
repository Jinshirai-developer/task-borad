using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskApi.Data;

namespace TaskApi.Services;

// Small-demo admission lock: serialize membership, quotas and workspace writes, including
// account removal. The database lock also covers separate application instances.
public sealed class WorkspaceWriteScope : IDisposable
{
    private static readonly object Admission = new();
    private const long AdvisoryLockKey = 2026090801;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    private WorkspaceWriteScope(AppDbContext context)
    {
        Monitor.Enter(Admission);
        try
        {
            if (context.Database.IsRelational())
            {
                if (context.Database.CurrentTransaction == null)
                    _transaction = context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
                if (context.Database.IsNpgsql())
                    context.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})");
            }
        }
        catch
        {
            _transaction?.Dispose();
            Monitor.Exit(Admission);
            throw;
        }
    }

    public static WorkspaceWriteScope Begin(AppDbContext context) => new(context);

    public void Commit() => _transaction?.Commit();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _transaction?.Dispose(); }
        finally { Monitor.Exit(Admission); }
    }
}
