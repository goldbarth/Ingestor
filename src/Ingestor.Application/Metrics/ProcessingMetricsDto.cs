namespace Ingestor.Application.Metrics;

public sealed record ProcessingMetricsDto(
    int TotalAttempts,
    int SuccessfulAttempts,
    int FailedAttempts,
    double AverageDurationMs);