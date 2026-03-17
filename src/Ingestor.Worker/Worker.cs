using Ingestor.Application.Abstractions;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.Extensions.Options;

namespace Ingestor.Worker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger,
    IOptions<WorkerOptions> options) : BackgroundService
{
    private TimeSpan PollingInterval => TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds);

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
        var jobRepository = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var attemptRepository = scope.ServiceProvider.GetRequiredService<IImportAttemptRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var pipelineHandler = scope.ServiceProvider.GetRequiredService<ImportPipelineHandler>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<Domain.Common.IClock>();

        var entry = await outboxRepository.ClaimNextAsync(ct);
        if (entry is null)
            return;

        logger.LogInformation("Processing job {JobId} (attempt {Attempt}).", entry.JobId.Value, entry.JobId);

        var startedAt = clock.UtcNow;

        try
        {
            var result = await pipelineHandler.HandleAsync(entry.JobId, ct);
            var finishedAt = clock.UtcNow;

            var attempt = result.IsSuccess
                ? ImportAttempt.Succeeded(ImportAttemptId.New(), entry.JobId,
                    attemptNumber: 1, startedAt, finishedAt)
                : ImportAttempt.Failed(ImportAttemptId.New(), entry.JobId,
                    attemptNumber: 1, startedAt, finishedAt,
                    ErrorCategory.Permanent, result.ErrorCode!, result.ErrorMessage!);

            await attemptRepository.AddAsync(attempt, ct);
            await outboxRepository.MarkAsDoneAsync(entry.Id, ct);
            await unitOfWork.SaveChangesAsync(ct);

            if (result.IsSuccess)
                logger.LogInformation("Job {JobId} succeeded. Items: {Count}.", entry.JobId.Value, result.ProcessedItemCount);
            else
                logger.LogWarning("Job {JobId} permanently failed: {ErrorCode}.", entry.JobId.Value, result.ErrorCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var finishedAt = clock.UtcNow;
            logger.LogError(ex, "Job {JobId} failed with transient error.", entry.JobId.Value);

            var job = await jobRepository.GetByIdAsync(entry.JobId, ct);
            if (job is null)
                return;

            job.RecordFailure("worker.transient_error", ex.Message);

            var attempt = ImportAttempt.Failed(ImportAttemptId.New(), entry.JobId,
                job.CurrentAttempt, startedAt, finishedAt,
                ErrorCategory.Transient, "worker.transient_error", ex.Message);

            await attemptRepository.AddAsync(attempt, ct);

            if (job.CurrentAttempt < job.MaxAttempts)
            {
                var delay = RetryPolicy.CalculateDelay(job.CurrentAttempt);
                var retryEntry = new OutboxEntry(
                    OutboxEntryId.New(), job.Id, finishedAt,
                    scheduledFor: finishedAt.Add(delay));

                job.TransitionTo(JobStatus.ProcessingFailed, finishedAt);
                await outboxRepository.AddAsync(retryEntry, ct);

                logger.LogInformation("Job {JobId} scheduled for retry in {Delay}s (attempt {Attempt}/{Max}).",
                    job.Id.Value, delay.TotalSeconds, job.CurrentAttempt, job.MaxAttempts);
            }
            else
            {
                var deadLetterEntry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, finishedAt);
                await deadLetterRepository.AddAsync(deadLetterEntry, ct);
                job.TransitionTo(JobStatus.DeadLettered, finishedAt);
                logger.LogWarning("Job {JobId} dead-lettered after {Max} attempts.", job.Id.Value, job.MaxAttempts);
            }

            await outboxRepository.MarkAsDoneAsync(entry.Id, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}