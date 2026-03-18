using Ingestor.Application.Metrics;
using Ingestor.Contracts.V1.Responses;

namespace Ingestor.Api.Endpoints;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/metrics");

        group.MapGet("jobs", GetJobMetricsAsync);
        group.MapGet("processing", GetProcessingMetricsAsync);
    }

    private static async Task<IResult> GetJobMetricsAsync(
        GetJobMetricsHandler handler,
        CancellationToken ct)
    {
        var counts = await handler.HandleAsync(ct);
        var countsByStatus = counts.ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => kvp.Value);

        return Results.Ok(new JobMetricsResponse(countsByStatus, countsByStatus.Values.Sum()));
    }

    private static async Task<IResult> GetProcessingMetricsAsync(
        GetProcessingMetricsHandler handler,
        CancellationToken ct)
    {
        var dto = await handler.HandleAsync(ct);
        var successRate = dto.TotalAttempts > 0
            ? Math.Round((double)dto.SuccessfulAttempts / dto.TotalAttempts, 3)
            : 0;

        return Results.Ok(new ProcessingMetricsResponse(
            dto.TotalAttempts,
            dto.SuccessfulAttempts,
            dto.FailedAttempts,
            successRate,
            dto.AverageDurationMs));
    }
}