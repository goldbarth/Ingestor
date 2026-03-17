using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfImportAttemptRepository(IngestorDbContext dbContext) : IImportAttemptRepository
{
    public async Task AddAsync(ImportAttempt attempt, CancellationToken ct = default)
    {
        await dbContext.ImportAttempts.AddAsync(attempt, ct);
    }
}