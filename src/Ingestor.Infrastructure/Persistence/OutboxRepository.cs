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
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
        
        // Set the lock
        var entry = await dbContext.OutboxEntries
            .FromSqlRaw("""
                SELECT "Id", "JobId", "Status", "AttemptNumber", "CreatedAt", "ScheduledFor", "LockedAt", "ProcessedAt"
                FROM outbox_entries
                WHERE "Status" = 'Pending'
                  AND ("ScheduledFor" IS NULL OR "ScheduledFor" <= NOW())
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .FirstOrDefaultAsync(ct);

        if (entry is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        // Save the row
        entry.Claim(clock.UtcNow);
        await dbContext.SaveChangesAsync(ct);
        // Release the lock
        await tx.CommitAsync(ct);

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

    public async Task MarkAsDoneByJobAsync(JobId jobId, CancellationToken ct = default)
    {
        var entry = await dbContext.OutboxEntries
            .FirstOrDefaultAsync(e => e.JobId == jobId && e.Status == OutboxStatus.Processing, ct);
        
        if (entry is null)
            return;
        
        entry.Complete(clock.UtcNow);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> RecoverStaleAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        return await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE outbox_entries
            SET "Status" = 'Pending', "LockedAt" = NULL
            WHERE "Id" IN (
                SELECT "Id" FROM outbox_entries
                WHERE "Status" = 'Processing'
                  AND "LockedAt" < NOW() - {timeout}
                FOR UPDATE SKIP LOCKED
            )
            """, ct);
    }
}