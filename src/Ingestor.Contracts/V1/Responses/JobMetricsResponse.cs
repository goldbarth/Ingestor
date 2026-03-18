namespace Ingestor.Contracts.V1.Responses;

public sealed record JobMetricsResponse(
    IReadOnlyDictionary<string, int> CountsByStatus,
    int Total);