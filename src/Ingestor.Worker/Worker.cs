using Ingestor.Application.Abstractions;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Ingestor.Worker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger,
    IOptions<WorkerOptions> options,
    WorkerHeartbeat heartbeat) : BackgroundService
{
    private TimeSpan PollingInterval => TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Import worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            heartbeat.Beat();
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error in poll cycle; resuming in {Delay}s.",
                    options.Value.PollingIntervalSeconds);
            }
            await Task.Delay(PollingInterval, stoppingToken);
        }

        logger.LogInformation("Import worker stopped.");
    }

    private async Task ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobDispatcher = scope.ServiceProvider.GetRequiredService<IJobDispatcher>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var jobRepository = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var attemptRepository = scope.ServiceProvider.GetRequiredService<IImportAttemptRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var exceptionClassifier = scope.ServiceProvider.GetRequiredService<IExceptionClassifier>();
        var pipelineHandler = scope.ServiceProvider.GetRequiredService<ImportPipelineHandler>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<Domain.Common.IClock>();

        var auditEventRepository = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();

        var recovered = await outboxRepository.RecoverStaleAsync(
            TimeSpan.FromSeconds(options.Value.StaleLockTimeoutSeconds), ct);
        if (recovered > 0)
            logger.LogInformation("Recovered {Count} stale outbox entr{Suffix}.",
                recovered, recovered == 1 ? "y" : "ies");

        var entry = await outboxRepository.ClaimNextAsync(ct);
        if (entry is null)
            return;
        
        var job = await jobRepository.GetByIdAsync(entry.JobId, ct);
        if (job is null)
            return;

        using var jobIdContext = LogContext.PushProperty("JobId", entry.JobId.Value);

        logger.LogInformation("Processing job {JobId} (attempt {Attempt}).", entry.JobId.Value, entry.AttemptNumber);

        var startedAt = clock.UtcNow;

        try
        {
            var result = await pipelineHandler.HandleAsync(entry.JobId, ct);
            var finishedAt = clock.UtcNow;

            var attempt = result.IsSuccess
                ? ImportAttempt.Succeeded(ImportAttemptId.New(), entry.JobId,
                    entry.AttemptNumber, startedAt, finishedAt)
                : ImportAttempt.Failed(ImportAttemptId.New(), entry.JobId,
                    entry.AttemptNumber, startedAt, finishedAt,
                    ErrorCategory.Permanent, result.ErrorCode!, result.ErrorMessage!);
            

            await attemptRepository.AddAsync(attempt, ct);
            await jobDispatcher.AcknowledgeAsync(job, ct);
            await unitOfWork.SaveChangesAsync(ct);

            if (result.IsSuccess)
                logger.LogInformation("Job {JobId} succeeded. Items: {Count}.", entry.JobId.Value, result.ProcessedItemCount);
            else
                logger.LogWarning("Job {JobId} permanently failed: {ErrorCode}.", entry.JobId.Value, result.ErrorCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var finishedAt = clock.UtcNow;
            var category = exceptionClassifier.Classify(ex);
            var errorCode = category == ErrorCategory.Transient ? "worker.transient_error" : "worker.unexpected_error";

            logger.LogError(ex, "Job {JobId} failed with {Category} error.", entry.JobId.Value, category);

            job.RecordFailure(errorCode, ex.Message);

            var attempt = ImportAttempt.Failed(ImportAttemptId.New(), entry.JobId,
                job.CurrentAttempt, startedAt, finishedAt,
                category, errorCode, ex.Message);

            await attemptRepository.AddAsync(attempt, ct);

            var preTransitionStatus = job.Status;

            if (category == ErrorCategory.Transient && job.CurrentAttempt < job.MaxAttempts)
            {
                job.TransitionTo(JobStatus.ProcessingFailed, finishedAt);
                await auditEventRepository.AddAsync(new AuditEvent(
                    AuditEventId.New(), job.Id, preTransitionStatus, JobStatus.ProcessingFailed,
                    AuditEventTrigger.Worker, finishedAt, ex.Message), ct);
                await jobDispatcher.DispatchAsync(job, ct);
                logger.LogInformation("Job {JobId} scheduled for retry.",
                    job.Id.Value);
            }
            else
            {
                logger.LogInformation("Job {JobId} failed after {Attempts} attempts.",
                    job.Id.Value, job.CurrentAttempt);
                var deadLetterEntry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, finishedAt);
                await deadLetterRepository.AddAsync(deadLetterEntry, ct);
                job.TransitionTo(JobStatus.DeadLettered, finishedAt);
                await auditEventRepository.AddAsync(new AuditEvent(
                    AuditEventId.New(), job.Id, preTransitionStatus, JobStatus.DeadLettered,
                    AuditEventTrigger.Worker, finishedAt,
                    $"Exhausted {job.CurrentAttempt}/{job.MaxAttempts} attempts"), ct);

                logger.LogWarning("Job {JobId} dead-lettered ({Category}, attempt {Attempt}/{Max}).",
                    job.Id.Value, category, job.CurrentAttempt, job.MaxAttempts);
            }

            await jobDispatcher.AcknowledgeAsync(job, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}