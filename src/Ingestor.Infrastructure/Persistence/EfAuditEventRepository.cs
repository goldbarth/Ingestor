using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfAuditEventRepository(IngestorDbContext dbContext) : IAuditEventRepository
{
    public async Task AddAsync(AuditEvent entry, CancellationToken ct = default)
    {
        await dbContext.AuditEvents.AddAsync(entry, ct);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetByJobIdAsync(JobId jobId, CancellationToken ct = default)
    {
        return await dbContext.AuditEvents
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);
    }
}