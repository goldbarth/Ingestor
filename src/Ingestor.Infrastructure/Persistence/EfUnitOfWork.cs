using Ingestor.Application.Abstractions;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(IngestorDbContext dbContext) : IUnitOfWork, IAfterSaveCallbackRegistry
{
    private readonly List<Func<CancellationToken, Task>> _afterSaveCallbacks = [];

    public void OnAfterSave(Func<CancellationToken, Task> action)
        => _afterSaveCallbacks.Add(action);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await dbContext.SaveChangesAsync(ct);
        foreach (var cb in _afterSaveCallbacks)
            await cb(ct);
        _afterSaveCallbacks.Clear();
    }
}
