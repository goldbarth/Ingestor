using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;
using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Jobs.CreateImportJob;

public sealed class CreateImportJobHandler(
    IImportJobRepository jobRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<JobId>> HandleAsync(CreateImportJobCommand command, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var idempotencyKey = ComputeIdempotencyKey(command.SupplierCode, command.RawData);

        if (await jobRepository.ExistsByIdempotencyKeyAsync(idempotencyKey, ct))
            return Result<JobId>.Conflict("job.duplicate", "A job with this file and supplier already exists.");

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
            now);

        await jobRepository.AddAsync(job, payload, ct);
        await outboxRepository.AddAsync(outboxEntry, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<JobId>.Success(jobId);
    }

    private static string ComputeIdempotencyKey(string supplierCode, byte[] rawData)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(rawData);
        return $"{supplierCode}:{Convert.ToHexString(hash)}";
    }
}
