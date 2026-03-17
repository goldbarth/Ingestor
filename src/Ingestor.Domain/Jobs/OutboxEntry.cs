using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Domain.Jobs;

public sealed class OutboxEntry
{
    public OutboxEntryId Id { get; private set; }
    public JobId JobId { get; private set; }
    public OutboxStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

#pragma warning disable CS8618
    private OutboxEntry() { }
#pragma warning restore CS8618

    public OutboxEntry(OutboxEntryId id, JobId jobId, DateTimeOffset createdAt)
    {
        Id = id;
        JobId = jobId;
        Status = OutboxStatus.Pending;
        CreatedAt = createdAt;
    }

    public void Claim(DateTimeOffset now)
    {
        Status = OutboxStatus.Processing;
        LockedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OutboxStatus.Done;
        ProcessedAt = now;
    }
}
