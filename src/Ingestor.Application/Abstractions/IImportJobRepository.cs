using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Abstractions;

public interface IImportJobRepository
{
    Task AddAsync(ImportJob job, ImportPayload payload, CancellationToken ct = default);
    Task<ImportJob?> GetByIdAsync(JobId id, CancellationToken ct = default);
    Task<ImportPayload?> GetPayloadByJobIdAsync(JobId jobId, CancellationToken ct = default);
    Task<ImportJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<ImportJob>> SearchAsync(
        JobStatus? status,
        JobId? cursor,
        int pageSize,
        CancellationToken ct = default);
}
