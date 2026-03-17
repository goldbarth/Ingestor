using Ingestor.Application.Abstractions;
using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class OutboxRepository(IngestorDbContext dbContext, IClock clock) : IOutboxRepository
{
    public async Task AddAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        await dbContext.OutboxEntries.AddAsync(entry, ct);
    }

    public async Task<OutboxEntry?> ClaimNextAsync(CancellationToken ct = default)
    {
        var entry = await dbContext.OutboxEntries
            .Where(e => e.Status == OutboxStatus.Pending)
            .OrderBy(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (entry is null)
            return null;

        // Safely claim the next pending outbox entry to prevent race conditions for worker threads
        entry.Claim(clock.UtcNow);
        await dbContext.SaveChangesAsync(ct);

        return entry;
    }

    public async Task MarkAsDoneAsync(OutboxEntryId id, CancellationToken ct = default)
    {
        var entry = await dbContext.OutboxEntries
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null)
            return;

        entry.Complete(clock.UtcNow);
        await dbContext.SaveChangesAsync(ct);
    }
}