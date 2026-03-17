namespace Ingestor.Domain.Jobs;

public readonly record struct ImportAttemptId(Guid Value)
{
    public static ImportAttemptId New() => new(Guid.CreateVersion7());
}