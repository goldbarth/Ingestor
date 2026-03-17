using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IOutboxRepository
{
    Task AddAsync(OutboxEntry entry, CancellationToken ct = default);
    Task<OutboxEntry?> ClaimNextAsync(CancellationToken ct = default);
    Task MarkAsDoneAsync(OutboxEntryId id, CancellationToken ct = default);
}
