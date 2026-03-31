namespace Ingestor.Application.Jobs;

public sealed class ImportJobOptions
{
    public const string SectionName = "Import";

    public int MaxAttempts { get; init; } = 3;
}
