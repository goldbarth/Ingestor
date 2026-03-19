namespace Ingestor.Contracts.V1.Responses;

public sealed record AuditEventResponse(
    DateTimeOffset OccurredAt,
    string OldStatus,
    string NewStatus,
    string TriggeredBy,
    string? Comment);