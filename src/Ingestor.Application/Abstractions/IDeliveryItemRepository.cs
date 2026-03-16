using Ingestor.Domain.DeliveryItems;

namespace Ingestor.Application.Abstractions;

public interface IDeliveryItemRepository
{
    Task AddRangeAsync(IReadOnlyList<DeliveryItem> items, CancellationToken ct = default);
}