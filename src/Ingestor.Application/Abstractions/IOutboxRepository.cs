using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IOutboxRepository
{
    Task AddAsync(OutboxEntry entry, CancellationToken ct = default);
}
