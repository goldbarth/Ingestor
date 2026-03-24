using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Abstractions;

public interface IJobDispatcher
{
    Task DispatchAsync(ImportJob job, CancellationToken ct = default);
    Task AcknowledgeAsync(ImportJob job, CancellationToken ct = default);
}