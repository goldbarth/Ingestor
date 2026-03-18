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

            job.TransitionTo(JobStatus.Parsing, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);

            var parser = job.ImportType == ImportType.CsvDeliveryAdvice ? csvParser : jsonParser;
            var parseResult = parser.Parse(new MemoryStream(payload.RawData));

            if (!parseResult.IsSuccess)
            {
                var parseErrorMessage = $"Parsing failed with {parseResult.Errors.Count} error(s).";
                parsingActivity?.SetStatus(ActivityStatusCode.Error, parseErrorMessage);
                job.RecordPermanentFailure("pipeline.parse_failed", parseErrorMessage);
                job.TransitionTo(JobStatus.ValidationFailed, clock.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                return PipelineResult.Failed("pipeline.parse_failed", parseErrorMessage);
            }

            // --- Validating ---
            const string pipelineValidating = "pipeline.validating";
            using (var validatingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineValidating))
            {
                validatingActivity?.SetTag("job.id", jobIdTag);

                job.TransitionTo(JobStatus.Validating, clock.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);

                var validationResult = validator.Validate(parseResult.Lines);

                if (!validationResult.IsValid)
                {
                    var validationErrorMessage = $"Validation failed with {validationResult.Errors.Count} error(s).";
                    validatingActivity?.SetStatus(ActivityStatusCode.Error, validationErrorMessage);
                    job.RecordPermanentFailure("pipeline.validation_failed", validationErrorMessage);
                    job.TransitionTo(JobStatus.ValidationFailed, clock.UtcNow);
                    await unitOfWork.SaveChangesAsync(ct);
                    return PipelineResult.Failed("pipeline.validation_failed", validationErrorMessage);
                }

                // --- Processing ---
                const string pipelineProcessing = "pipeline.processing";
                using (var processingActivity = IngestorActivitySource.Pipeline.StartActivity(pipelineProcessing))
                {
                    processingActivity?.SetTag("job.id", jobIdTag);

                    job.TransitionTo(JobStatus.Processing, clock.UtcNow);
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
                    job.TransitionTo(JobStatus.Succeeded, clock.UtcNow, items.Count);
                    await unitOfWork.SaveChangesAsync(ct);

                    processingActivity?.SetTag("job.processed_item_count", items.Count);
                    return PipelineResult.Success(items.Count);
                }
            }
        }
    }
}
