using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfDeadLetterRepository(IngestorDbContext dbContext) : IDeadLetterRepository
{
    public async Task AddAsync(DeadLetterEntry entry, CancellationToken ct = default)
    {
        await dbContext.DeadLetterEntries.AddAsync(entry, ct);
    }
}