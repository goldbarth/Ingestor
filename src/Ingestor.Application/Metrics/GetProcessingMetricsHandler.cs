using Ingestor.Application.Abstractions;

namespace Ingestor.Application.Metrics;

public sealed class GetProcessingMetricsHandler(IImportAttemptRepository attemptRepository)
{
    public async Task<ProcessingMetricsDto> HandleAsync(CancellationToken ct = default)
        => await attemptRepository.GetProcessingMetricsAsync(ct);
}