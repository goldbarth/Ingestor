using System.Collections.Concurrent;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqDeliveryTagStore
{
    private readonly ConcurrentDictionary<Guid, ulong> _tags = new();
    
    public void Register(JobId jobId, ulong deliveryTag)
        => _tags[jobId.Value] = deliveryTag;
    
    public bool TryRemove(JobId jobId, out ulong deliveryTag)
        => _tags.TryRemove(jobId.Value, out deliveryTag);
}