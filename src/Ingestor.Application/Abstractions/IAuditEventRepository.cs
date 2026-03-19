using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IAuditEventRepository
{
    Task AddAsync(AuditEvent entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEvent>> GetByJobIdAsync(JobId jobId, CancellationToken ct = default);
}