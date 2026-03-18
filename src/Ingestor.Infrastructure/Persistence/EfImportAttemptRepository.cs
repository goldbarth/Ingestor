using Ingestor.Application.Abstractions;
using Ingestor.Application.Metrics;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfImportAttemptRepository(IngestorDbContext dbContext) : IImportAttemptRepository
{
    public async Task AddAsync(ImportAttempt attempt, CancellationToken ct = default)
    {
        await dbContext.ImportAttempts.AddAsync(attempt, ct);
    }

    public async Task<ProcessingMetricsDto> GetProcessingMetricsAsync(CancellationToken ct = default)
    {
        var stats = await dbContext.ImportAttempts
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Succeeded = g.Count(a => a.Outcome == AttemptOutcome.Succeeded),
                AvgDuration = g.Average(a => (double)a.DurationMs)
            })
            .FirstOrDefaultAsync(ct);

        if (stats is null)
            return new ProcessingMetricsDto(0, 0, 0, 0);

        return new ProcessingMetricsDto(
            stats.Total,
            stats.Succeeded,
            stats.Total - stats.Succeeded,
            Math.Round(stats.AvgDuration, 1));
    }
}