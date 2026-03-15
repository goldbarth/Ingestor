namespace Ingestor.Domain.Jobs;

public readonly record struct OutboxEntryId(Guid Value)
{
    public static OutboxEntryId New() => new(Guid.CreateVersion7());
}
