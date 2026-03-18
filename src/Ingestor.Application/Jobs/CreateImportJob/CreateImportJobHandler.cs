using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;
using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Jobs.CreateImportJob;

public sealed class CreateImportJobHandler(
    IImportJobRepository jobRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CreateImportJobResult>> HandleAsync(CreateImportJobCommand command, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var idempotencyKey = IdempotencyKeyComputer.Compute(command.SupplierCode, command.RawData);

        var existingJob = await jobRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existingJob is not null)
            return Result<CreateImportJobResult>.Success(new(existingJob.Id, IsNew: false, existingJob.Status));

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

        var outboxEntry = new OutboxEntry(
            OutboxEntryId.New(),
            jobId,
            now,
            attemptNumber: 1);

        await jobRepository.AddAsync(job, payload, ct);
        await outboxRepository.AddAsync(outboxEntry, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<CreateImportJobResult>.Success(new(jobId, IsNew: true));
    }

}
