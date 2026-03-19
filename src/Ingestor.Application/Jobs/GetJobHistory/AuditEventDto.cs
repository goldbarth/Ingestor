using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.GetJobHistory;

public sealed record AuditEventDto(
    DateTimeOffset OccurredAt,
    JobStatus OldStatus,
    JobStatus NewStatus,
    AuditEventTrigger TriggeredBy,
    string? Comment);