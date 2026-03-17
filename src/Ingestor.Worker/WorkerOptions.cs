namespace Ingestor.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollingIntervalSeconds { get; init; } = 5;
}