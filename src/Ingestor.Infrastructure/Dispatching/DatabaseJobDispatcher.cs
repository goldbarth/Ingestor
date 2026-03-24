using Ingestor.Application.Abstractions;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Dispatching;

internal sealed class DatabaseJobDispatcher(
    IOutboxRepository outboxRepository,
    IClock clock) : IJobDispatcher
{
    public async Task DispatchAsync(ImportJob job, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var delay = RetryPolicy.CalculateDelay(job.CurrentAttempt);

        var entry = new OutboxEntry(
            OutboxEntryId.New(),
            job.Id,
            now,
            attemptNumber: job.CurrentAttempt + 1,
            scheduledFor: job.CurrentAttempt > 0 ? now.Add(delay) : null);

        await outboxRepository.AddAsync(entry, ct);
    }

    public async Task AcknowledgeAsync(ImportJob job, CancellationToken ct = default)
    {
        await outboxRepository.MarkAsDoneByJobAsync(job.Id, ct);
    }
}