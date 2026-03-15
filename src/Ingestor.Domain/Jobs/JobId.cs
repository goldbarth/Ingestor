namespace Ingestor.Domain.Jobs;

public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new JobId(Guid.CreateVersion7());
}