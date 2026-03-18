using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

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

    public async Task<ImportPayload?> GetPayloadByJobIdAsync(JobId jobId, CancellationToken ct = default)
    {
        return await dbContext.ImportPayloads
            .FirstOrDefaultAsync(p => p.JobId == jobId, ct);
    }

    public async Task<ImportJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        return await dbContext.ImportJobs
            .FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<IReadOnlyDictionary<JobStatus, int>> GetStatusCountsAsync(CancellationToken ct = default)
    {
        var counts = await dbContext.ImportJobs
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ReadOnlyDictionary<JobStatus, int>(
            counts.ToDictionary(x => x.Status, x => x.Count));
    }

    public async Task<IReadOnlyList<ImportJob>> SearchAsync(
        JobStatus? status,
        JobId? cursor,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = dbContext.ImportJobs.AsQueryable();

        if (status.HasValue)
            query = query.Where(j => j.Status == status.Value);

        if (cursor.HasValue)
            query = query.Where(j => j.Id.Value > cursor.Value.Value);

        return await query
            .OrderBy(j => j.Id)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
