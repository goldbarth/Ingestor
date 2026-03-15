namespace Ingestor.Domain.Jobs;

public readonly record struct PayloadId(Guid Value)
{
    public static PayloadId New() => new PayloadId(Guid.CreateVersion7());
}