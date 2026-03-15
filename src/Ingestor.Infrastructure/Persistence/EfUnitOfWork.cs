using Ingestor.Application.Abstractions;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(IngestorDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default)
        => dbContext.SaveChangesAsync(ct);
}
