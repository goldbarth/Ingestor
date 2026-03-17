using Ingestor.Application.Abstractions;
using Ingestor.Application.Pipeline;

namespace Ingestor.Worker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Import worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessNextAsync(stoppingToken);
            await Task.Delay(PollingInterval, stoppingToken);
        }

        logger.LogInformation("Import worker stopped.");
    }

    private async Task ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var pipelineHandler = scope.ServiceProvider.GetRequiredService<ImportPipelineHandler>();

        var entry = await outboxRepository.ClaimNextAsync(ct);

        if (entry is null)
            return;

        logger.LogInformation("Processing job {JobId}.", entry.JobId.Value);

        var result = await pipelineHandler.HandleAsync(entry.JobId, ct);

        if (result.IsSuccess)
        {
            await outboxRepository.MarkAsDoneAsync(entry.Id, ct);
            logger.LogInformation("Job {JobId} succeeded. Items processed: {Count}.",
                entry.JobId.Value, result.ProcessedItemCount);
        }
        else
        {
            logger.LogWarning("Job {JobId} failed. Error: {ErrorCode} — {ErrorMessage}.",
                entry.JobId.Value, result.ErrorCode, result.ErrorMessage);
        }
    }
}
