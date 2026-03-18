namespace Ingestor.Contracts.V1.Responses;

public sealed record ProcessingMetricsResponse(
    int TotalAttempts,
    int SuccessfulAttempts,
    int FailedAttempts,
    double SuccessRate,
    double AverageDurationMs);