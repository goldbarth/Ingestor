using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.CreateImportJob;

public sealed class CreateImportJobHandler(
    IImportJobRepository jobRepository,
    IJobDispatcher jobDispatcher,
    IUnitOfWork unitOfWork)
{
    private const int MaxPayloadSizeBytes = 10 * 1024 * 1024;

    public async Task<Result<CreateImportJobResult>> HandleAsync(CreateImportJobCommand command, CancellationToken ct = default)
    {
        if (command.RawData.Length > MaxPayloadSizeBytes)
            return Result<CreateImportJobResult>.Validation(
                "job.payload_too_large",
                $"Payload size {command.RawData.Length / 1024 / 1024} MB exceeds the {MaxPayloadSizeBytes / 1024 / 1024} MB limit.");

        var now = DateTimeOffset.UtcNow;
        var idempotencyKey = IdempotencyKeyComputer.Compute(command.SupplierCode, command.RawData);

        var existingJob = await jobRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existingJob is not null)
        {
            if (existingJob.Status == JobStatus.DeadLettered)
                return Result<CreateImportJobResult>.Conflict(
                    "job.dead_lettered",
                    $"A previous submission with this file is dead-lettered. Contact an operator to requeue job '{existingJob.Id.Value}'.");

            return Result<CreateImportJobResult>.Success(new(existingJob.Id, IsNew: false, existingJob.Status));
        }

        var jobId = JobId.New();

        var payload = new ImportPayload(
            PayloadId.New(),
            jobId,
            command.ContentType,
            command.RawData,
            now);

        var job = new ImportJob(
            jobId,
            command.SupplierCode,
            command.ImportType,
            idempotencyKey,
            payload.Id.Value.ToString(),
            now,
            maxAttempts: 3);

        await jobRepository.AddAsync(job, payload, ct);
        await jobDispatcher.DispatchAsync(job, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<CreateImportJobResult>.Success(new(jobId, IsNew: true));
    }

}
