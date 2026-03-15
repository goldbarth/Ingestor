using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class OutboxRepository(IngestorDbContext dbContext) : IOutboxRepository
{
    public async Task AddAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        await dbContext.OutboxEntries.AddAsync(entry, ct);
    }
}
