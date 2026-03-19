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

namespace Ingestor.Application.Pipeline;

public sealed class ImportPipelineHandler(
    IImportJobRepository jobRepository,
    IDeliveryItemRepository deliveryItemRepository,
    IAuditEventRepository auditEventRepository,
    IUnitOfWork unitOfWork,
    DeliveryAdviceValidator validator,
    IClock clock,
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

        // --- Parsing ---
        const string pipelineParsing = "pipeline.parsing";
        using (var parsingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineParsing))
        {
            parsingActivity?.SetTag("job.id", jobIdTag);

            var parsingNow = clock.UtcNow;
            var preParsingStatus = job.Status;
            job.TransitionTo(JobStatus.Parsing, parsingNow);
            await auditEventRepository.AddAsync(new AuditEvent(
                AuditEventId.New(), job.Id, preParsingStatus, JobStatus.Parsing,
                AuditEventTrigger.Worker, parsingNow), ct);
            await unitOfWork.SaveChangesAsync(ct);

            var parser = job.ImportType == ImportType.CsvDeliveryAdvice ? csvParser : jsonParser;
            var parseResult = parser.Parse(new MemoryStream(payload.RawData));

            if (!parseResult.IsSuccess)
            {
                var parseErrorMessage = $"Parsing failed with {parseResult.Errors.Count} error(s).";
                parsingActivity?.SetStatus(ActivityStatusCode.Error, parseErrorMessage);
                var parseFailNow = clock.UtcNow;
                var preParseFailStatus = job.Status;
                job.RecordPermanentFailure("pipeline.parse_failed", parseErrorMessage);
                job.TransitionTo(JobStatus.ValidationFailed, parseFailNow);
                await auditEventRepository.AddAsync(new AuditEvent(
                    AuditEventId.New(), job.Id, preParseFailStatus, JobStatus.ValidationFailed,
                    AuditEventTrigger.Worker, parseFailNow, parseErrorMessage), ct);
                await unitOfWork.SaveChangesAsync(ct);
                return PipelineResult.Failed("pipeline.parse_failed", parseErrorMessage);
            }

            // --- Validating ---
            const string pipelineValidating = "pipeline.validating";
            using (var validatingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineValidating))
            {
                validatingActivity?.SetTag("job.id", jobIdTag);

                var validatingNow = clock.UtcNow;
                var preValidatingStatus = job.Status;
                job.TransitionTo(JobStatus.Validating, validatingNow);
                await auditEventRepository.AddAsync(new AuditEvent(
                    AuditEventId.New(), job.Id, preValidatingStatus, JobStatus.Validating,
                    AuditEventTrigger.Worker, validatingNow), ct);
                await unitOfWork.SaveChangesAsync(ct);

                var validationResult = validator.Validate(parseResult.Lines);

                if (!validationResult.IsValid)
                {
                    var validationErrorMessage = $"Validation failed with {validationResult.Errors.Count} error(s).";
                    validatingActivity?.SetStatus(ActivityStatusCode.Error, validationErrorMessage);
                    var validationFailNow = clock.UtcNow;
                    var preValidationFailStatus = job.Status;
                    job.RecordPermanentFailure("pipeline.validation_failed", validationErrorMessage);
                    job.TransitionTo(JobStatus.ValidationFailed, validationFailNow);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preValidationFailStatus, JobStatus.ValidationFailed,
                        AuditEventTrigger.Worker, validationFailNow, validationErrorMessage), ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    return PipelineResult.Failed("pipeline.validation_failed", validationErrorMessage);
                }

                // --- Processing ---
                const string pipelineProcessing = "pipeline.processing";
                using (var processingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineProcessing))
                {
                    processingActivity?.SetTag("job.id", jobIdTag);

                    var processingNow = clock.UtcNow;
                    var preProcessingStatus = job.Status;
                    job.TransitionTo(JobStatus.Processing, processingNow);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preProcessingStatus, JobStatus.Processing,
                        AuditEventTrigger.Worker, processingNow), ct);
                    await unitOfWork.SaveChangesAsync(ct);

                    var processedAt = clock.UtcNow;
                    var items = parseResult.Lines
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

                    await deliveryItemRepository.AddRangeAsync(items, ct);
                    var succeededNow = clock.UtcNow;
                    var preSucceededStatus = job.Status;
                    job.TransitionTo(JobStatus.Succeeded, succeededNow, items.Count);
                    await auditEventRepository.AddAsync(new AuditEvent(
                        AuditEventId.New(), job.Id, preSucceededStatus, JobStatus.Succeeded,
                        AuditEventTrigger.Worker, succeededNow), ct);
                    await unitOfWork.SaveChangesAsync(ct);

                    processingActivity?.SetTag("job.processed_item_count", items.Count);
                    return PipelineResult.Success(items.Count);
                }
            }
        }
    }
}
