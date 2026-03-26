namespace Ingestor.Application.Pipeline;

public sealed class BatchOptions
{
    public const string SectionName = "Batch";

    public int ChunkSize { get; init; } = 500;
}