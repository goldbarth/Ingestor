using Ingestor.Application.Metrics;
using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IImportAttemptRepository
{
    Task AddAsync(ImportAttempt attempt, CancellationToken ct = default);
    Task<ProcessingMetricsDto> GetProcessingMetricsAsync(CancellationToken ct = default);
}