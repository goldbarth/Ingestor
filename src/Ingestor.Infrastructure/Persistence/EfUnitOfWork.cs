using Ingestor.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(IngestorDbContext dbContext) : IUnitOfWork, IAfterSaveCallbackRegistry
{
    private readonly List<Func<CancellationToken, Task>> _afterSaveCallbacks = [];

    public void OnAfterSave(Func<CancellationToken, Task> action)
        => _afterSaveCallbacks.Add(action);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch
        {
            // Detach every entity that was staged as Added but never written to the DB.
            // This prevents a subsequent SaveChangesAsync call (e.g. in a catch block)
            // from silently re-persisting entities that belong to the failed operation.
            // Modified entities are intentionally left tracked so callers can compensate.
            foreach (var entry in dbContext.ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
                entry.State = EntityState.Detached;
            throw;
        }

        foreach (var cb in _afterSaveCallbacks)
            await cb(ct);
        _afterSaveCallbacks.Clear();
    }
}
