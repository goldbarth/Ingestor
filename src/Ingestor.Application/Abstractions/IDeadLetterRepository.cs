using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IDeadLetterRepository
{
    Task AddAsync(DeadLetterEntry entry, CancellationToken ct = default);
}
