using System.Diagnostics;
using System.Text.Json;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Serilog.Context;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqWorker(
    RabbitMqConnectionManager connectionManager,
    RabbitMqDeliveryTagStore deliveryTagStore,
    RabbitMqOptions options,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqWorker> logger) : BackgroundService
{
    private IChannel? _channel;
    private CancellationToken _stoppingToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        while (true)
        {
            try
            {
                await StartConsumerAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (BrokerUnreachableException ex)
            {
                logger.LogWarning(ex, "RabbitMQ not reachable. Retrying in {Interval}s.",
                    options.InitialConnectionRetryIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(options.InitialConnectionRetryIntervalSeconds), stoppingToken);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        logger.LogInformation("RabbitMQ worker stopped.");
    }

    private async Task StartConsumerAsync(CancellationToken ct)
    {
        _channel = await connectionManager.GetConsumerChannelAsync(ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;

        await _channel.BasicConsumeAsync(queue: options.QueueName, autoAck: false, consumer: consumer, cancellationToken: ct);

        logger.LogInformation("RabbitMQ worker started, consuming from '{Queue}'.", options.QueueName);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        var ct = _stoppingToken;

        ImportJobMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ImportJobMessage>(args.Body.Span);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            using var unreadableActivity = RabbitMqTelemetry.StartConsumerActivity(
                IngestorMessagingActivitySource.Messaging,
                args,
                options);
            unreadableActivity?.SetStatus(ActivityStatusCode.Error, "Unreadable RabbitMQ message payload.");
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            logger.LogWarning("Discarded unreadable message with delivery tag {Tag}.", args.DeliveryTag);
            return;
        }

        var jobId = new JobId(message.JobId);
        using var activity = RabbitMqTelemetry.StartConsumerActivity(
            IngestorMessagingActivitySource.Messaging,
            args,
            options,
            jobId.Value);
        deliveryTagStore.Register(jobId, args.DeliveryTag);

        await using var scope = scopeFactory.CreateAsyncScope();
        var jobDispatcher        = scope.ServiceProvider.GetRequiredService<IJobDispatcher>();
        var jobRepository        = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var attemptRepository    = scope.ServiceProvider.GetRequiredService<IImportAttemptRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var exceptionClassifier  = scope.ServiceProvider.GetRequiredService<IExceptionClassifier>();
        var pipelineHandler      = scope.ServiceProvider.GetRequiredService<ImportPipelineHandler>();
        var unitOfWork           = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock                = scope.ServiceProvider.GetRequiredService<Domain.Common.IClock>();
        var auditEventRepository = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();

        var job = await jobRepository.GetByIdAsync(jobId, ct);
        if (job is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Referenced job was not found.");
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            logger.LogWarning("Job {JobId} not found; discarding message.", jobId.Value);
            return;
        }

        var attemptNumber = job.CurrentAttempt + 1;

        using var jobIdContext = LogContext.PushProperty("JobId", jobId.Value);
        logger.LogInformation("Processing job {JobId} (attempt {Attempt}).", jobId.Value, attemptNumber);

        var startedAt = clock.UtcNow;

        try
        {
            var result = await pipelineHandler.HandleAsync(jobId, ct);
            var finishedAt = clock.UtcNow;

            var attempt = result.IsSuccess
                ? ImportAttempt.Succeeded(ImportAttemptId.New(), jobId, attemptNumber, startedAt, finishedAt)
                : ImportAttempt.Failed(ImportAttemptId.New(), jobId, attemptNumber, startedAt, finishedAt,
                    ErrorCategory.Permanent, result.ErrorCode!, result.ErrorMessage!);

            await attemptRepository.AddAsync(attempt, ct);
            await jobDispatcher.AcknowledgeAsync(job, ct);
            await unitOfWork.SaveChangesAsync(ct);

            if (result.IsSuccess)
            {
                activity?.SetTag("job.processed_item_count", result.ProcessedItemCount);
                logger.LogInformation("Job {JobId} succeeded. Items: {Count}.", jobId.Value, result.ProcessedItemCount);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                activity?.SetTag("error.code", result.ErrorCode);
                logger.LogWarning("Job {JobId} permanently failed: {ErrorCode}.", jobId.Value, result.ErrorCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var finishedAt = clock.UtcNow;
            var category = exceptionClassifier.Classify(ex);
            var errorCode = category == ErrorCategory.Transient ? "worker.transient_error" : "worker.unexpected_error";

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.code", errorCode);
            logger.LogError(ex, "Job {JobId} failed with {Category} error.", jobId.Value, category);

            job.RecordFailure(errorCode, ex.Message);

            var attempt = ImportAttempt.Failed(ImportAttemptId.New(), jobId,
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
                logger.LogInformation("Job {JobId} scheduled for retry.", job.Id.Value);
            }
            else
            {
                logger.LogInformation("Job {JobId} failed after {Attempts} attempts.", job.Id.Value, job.CurrentAttempt);
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

            deliveryTagStore.TryRemove(job.Id, out _);
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
