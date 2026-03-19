namespace Ingestor.Domain.Jobs;

public readonly record struct AuditEventId(Guid Value)
{
    public static AuditEventId New() => new(Guid.CreateVersion7());
}