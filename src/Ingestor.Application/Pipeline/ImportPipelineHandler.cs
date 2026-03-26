using System.Diagnostics;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Telemetry;
using Ingestor.Domain.Common;
using Ingestor.Domain.DeliveryItems;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Domain.Parsing;
using Ingestor.Domain.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ingestor.Application.Pipeline;

public sealed class ImportPipelineHandler(
    IImportJobRepository jobRepository,
    IDeliveryItemRepository deliveryItemRepository,
    IAuditEventRepository auditEventRepository,
    IUnitOfWork unitOfWork,
    DeliveryAdviceValidator validator,
    IClock clock,
    IOptions<BatchOptions> batchOptions,
    [FromKeyedServices("csv")] IDeliveryAdviceParser csvParser,
    [FromKeyedServices("json")] IDeliveryAdviceParser jsonParser)
{
    public async Task<PipelineResult> HandleAsync(JobId jobId, CancellationToken ct = default)
    {
        var job = await jobRepository.GetByIdAsync(jobId, ct);
        if (job is null)
            return PipelineResult.Failed("pipeline.job_not_found", $"Job '{jobId.Value}' was not found.");

        var payload = await jobRepository.GetPayloadByJobIdAsync(jobId, ct);
        if (payload is null)
            return PipelineResult.Failed("pipeline.payload_not_found", $"Payload for job '{jobId.Value}' was not found.");

        var jobIdTag = jobId.Value.ToString();
        var pipelineStarted = Stopwatch.GetTimestamp();
        var pipelineOutcome = "success";
        string? pipelineErrorCode = null;
        var processedItemCount = 0;

        using var pipelineActivity = IngestorActivitySource.Pipeline.StartActivity("pipeline.run");
        pipelineActivity?.SetTag("job.id", jobIdTag);

        try
        {
            ParseResult<DeliveryAdviceLine> parseResult = default!;
            IReadOnlyList<IReadOnlyList<DeliveryAdviceLine>> chunks = [];

            // --- Parsing ---
            const string pipelineParsing = "pipeline.parsing";
            var parsingStarted = Stopwatch.GetTimestamp();
            var parsingOutcome = "success";

            using (var parsingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineParsing))
            {
                parsingActivity?.SetTag("job.id", jobIdTag);

                try
                {
                    var parsingNow = clock.UtcNow;
                    var preParsingStatus = job.Status;
                    job.TransitionTo(JobStatus.Parsing, parsingNow);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preParsingStatus, JobStatus.Parsing,
                        AuditEventTrigger.Worker, parsingNow), ct);
                    await unitOfWork.SaveChangesAsync(ct);

                    var parser = job.ImportType == ImportType.CsvDeliveryAdvice ? csvParser : jsonParser;
                    parseResult = parser.Parse(new MemoryStream(payload.RawData));

                    if (!parseResult.IsSuccess)
                    {
                        parsingOutcome = "error";
                        pipelineOutcome = "error";
                        pipelineErrorCode = "pipeline.parse_failed";

                        var parseErrorMessage = $"Parsing failed with {parseResult.Errors.Count} error(s).";
                        parsingActivity?.SetStatus(ActivityStatusCode.Error, parseErrorMessage);
                        pipelineActivity?.SetStatus(ActivityStatusCode.Error, parseErrorMessage);

                        var parseFailNow = clock.UtcNow;
                        var preParseFailStatus = job.Status;
                        job.RecordPermanentFailure(pipelineErrorCode, parseErrorMessage);
                        job.TransitionTo(JobStatus.ValidationFailed, parseFailNow);
                        await auditEventRepository.AddAsync(new AuditEvent(
                            AuditEventId.New(), job.Id, preParseFailStatus, JobStatus.ValidationFailed,
                            AuditEventTrigger.Worker, parseFailNow, parseErrorMessage), ct);
                        await unitOfWork.SaveChangesAsync(ct);

                        return PipelineResult.Failed(pipelineErrorCode, parseErrorMessage);
                    }

                    chunks = LineChunker.Split(parseResult.Lines, batchOptions.Value.ChunkSize);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    parsingOutcome = "exception";
                    parsingActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
                finally
                {
                    IngestorMeter.RecordPipelineStepDuration(
                        pipelineParsing,
                        parsingOutcome,
                        Stopwatch.GetElapsedTime(parsingStarted).TotalMilliseconds);
                }
            }

            // --- Validating ---
            const string pipelineValidating = "pipeline.validating";
            var validatingStarted = Stopwatch.GetTimestamp();
            var validatingOutcome = "success";

            using (var validatingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineValidating))
            {
                validatingActivity?.SetTag("job.id", jobIdTag);

                try
                {
                    var validatingNow = clock.UtcNow;
                    var preValidatingStatus = job.Status;
                    job.TransitionTo(JobStatus.Validating, validatingNow);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preValidatingStatus, JobStatus.Validating,
                        AuditEventTrigger.Worker, validatingNow), ct);
                    await unitOfWork.SaveChangesAsync(ct);

                    for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                    {
                        var validationResult = validator.Validate(chunks[chunkIndex]);

                        if (!validationResult.IsValid)
                        {
                            validatingOutcome = "error";
                            pipelineOutcome = "error";
                            pipelineErrorCode = "pipeline.validation_failed";

                            var validationErrorMessage =
                                $"Validation failed in chunk {chunkIndex + 1}/{chunks.Count} with {validationResult.Errors.Count} error(s).";
                            validatingActivity?.SetStatus(ActivityStatusCode.Error, validationErrorMessage);
                            pipelineActivity?.SetStatus(ActivityStatusCode.Error, validationErrorMessage);

                            var validationFailNow = clock.UtcNow;
                            var preValidationFailStatus = job.Status;
                            job.RecordPermanentFailure(pipelineErrorCode, validationErrorMessage);
                            job.TransitionTo(JobStatus.ValidationFailed, validationFailNow);
                            await auditEventRepository.AddAsync(new AuditEvent(
                                AuditEventId.New(), job.Id, preValidationFailStatus, JobStatus.ValidationFailed,
                                AuditEventTrigger.Worker, validationFailNow, validationErrorMessage), ct);
                            await unitOfWork.SaveChangesAsync(ct);

                            return PipelineResult.Failed(pipelineErrorCode, validationErrorMessage);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    validatingOutcome = "exception";
                    validatingActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
                finally
                {
                    IngestorMeter.RecordPipelineStepDuration(
                        pipelineValidating,
                        validatingOutcome,
                        Stopwatch.GetElapsedTime(validatingStarted).TotalMilliseconds);
                }
            }

            // --- Processing ---
            const string pipelineProcessing = "pipeline.processing";
            var processingStarted = Stopwatch.GetTimestamp();
            var processingOutcome = "success";

            using (var processingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineProcessing))
            {
                processingActivity?.SetTag("job.id", jobIdTag);

                try
                {
                    var processingNow = clock.UtcNow;
                    var preProcessingStatus = job.Status;
                    job.TransitionTo(JobStatus.Processing, processingNow);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preProcessingStatus, JobStatus.Processing,
                        AuditEventTrigger.Worker, processingNow), ct);
                    await unitOfWork.SaveChangesAsync(ct);

                    var processedAt = clock.UtcNow;
                    var totalCount = 0;

                    foreach (var chunk in chunks)
                    {
                        var chunkItems = chunk
                            .Select(line => new DeliveryItem(
                                DeliveryItemId.New(),
                                job.Id,
                                line.ArticleNumber,
                                line.ProductName,
                                line.Quantity,
                                line.ExpectedDate,
                                line.SupplierRef,
                                processedAt))
                            .ToList();

                        await deliveryItemRepository.AddRangeAsync(chunkItems, ct);
                        totalCount += chunkItems.Count;
                    }

                    var succeededNow = clock.UtcNow;
                    var preSucceededStatus = job.Status;
                    job.TransitionTo(JobStatus.Succeeded, succeededNow, totalCount);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preSucceededStatus, JobStatus.Succeeded,
                        AuditEventTrigger.Worker, succeededNow), ct);
                    await unitOfWork.SaveChangesAsync(ct);

                    processedItemCount = totalCount;
                    processingActivity?.SetTag("job.processed_item_count", processedItemCount);

                    return PipelineResult.Success(processedItemCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    processingOutcome = "exception";
                    processingActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
                finally
                {
                    IngestorMeter.RecordPipelineStepDuration(
                        pipelineProcessing,
                        processingOutcome,
                        Stopwatch.GetElapsedTime(processingStarted).TotalMilliseconds);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            pipelineOutcome = "exception";
            pipelineErrorCode ??= "pipeline.unhandled_exception";
            pipelineActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            pipelineActivity?.SetTag("job.outcome", pipelineOutcome);

            if (!string.IsNullOrWhiteSpace(pipelineErrorCode))
                pipelineActivity?.SetTag("error.code", pipelineErrorCode);

            if (processedItemCount > 0)
                pipelineActivity?.SetTag("job.processed_item_count", processedItemCount);

            IngestorMeter.RecordPipelineRun(
                pipelineOutcome,
                Stopwatch.GetElapsedTime(pipelineStarted).TotalMilliseconds,
                processedItemCount,
                pipelineErrorCode);
        }
    }
}
