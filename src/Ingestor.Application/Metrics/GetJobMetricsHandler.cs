using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Metrics;

public sealed class GetJobMetricsHandler(IImportJobRepository jobRepository)
{
    public async Task<IReadOnlyDictionary<JobStatus, int>> HandleAsync(CancellationToken ct = default)
        => await jobRepository.GetStatusCountsAsync(ct);
}