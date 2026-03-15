using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class ImportJobRepository(IngestorDbContext dbContext) : IImportJobRepository
{
    public async Task AddAsync(ImportJob job, ImportPayload payload, CancellationToken ct = default)
    {
        await dbContext.ImportJobs.AddAsync(job, ct);
        await dbContext.ImportPayloads.AddAsync(payload, ct);
    }

    public async Task<ImportJob?> GetByIdAsync(JobId id, CancellationToken ct = default)
    {
        return await dbContext.ImportJobs
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<bool> ExistsByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        return await dbContext.ImportJobs
            .AnyAsync(j => j.IdempotencyKey == idempotencyKey, ct);
    }
}
