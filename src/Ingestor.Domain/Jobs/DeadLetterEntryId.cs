namespace Ingestor.Domain.Jobs;

public readonly record struct DeadLetterEntryId(Guid Value)
{
    public static DeadLetterEntryId New() => new(Guid.CreateVersion7());
}