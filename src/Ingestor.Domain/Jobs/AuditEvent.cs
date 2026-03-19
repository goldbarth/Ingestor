using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Domain.Jobs;

public sealed class AuditEvent
{
    public AuditEventId Id { get; private set; }
    public JobId JobId { get; private set; }
    public JobStatus OldStatus { get; private set; }
    public JobStatus NewStatus { get; private set; }
    public AuditEventTrigger TriggeredBy { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Comment { get; private set; }

#pragma warning disable CS8618
    private AuditEvent() { }
#pragma warning restore CS8618

    public AuditEvent(
        AuditEventId id,
        JobId jobId,
        JobStatus oldStatus,
        JobStatus newStatus,
        AuditEventTrigger triggeredBy,
        DateTimeOffset occurredAt,
        string? comment = null)
    {
        Id = id;
        JobId = jobId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        TriggeredBy = triggeredBy;
        OccurredAt = occurredAt;
        Comment = comment;
    }
}