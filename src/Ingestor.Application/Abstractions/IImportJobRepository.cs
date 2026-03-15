using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IImportJobRepository
{
    Task AddAsync(ImportJob job, ImportPayload payload, CancellationToken ct = default);
    Task<ImportJob?> GetByIdAsync(JobId id, CancellationToken ct = default);
    Task<bool> ExistsByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
}
