using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfAuditEventRepository(IngestorDbContext dbContext) : IAuditEventRepository
{
    public async Task AddAsync(AuditEvent entry, CancellationToken ct = default)
    {
        await dbContext.AuditEvents.AddAsync(entry, ct);
    }
}