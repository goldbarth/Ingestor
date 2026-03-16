using Ingestor.Application.Abstractions;
using Ingestor.Domain.DeliveryItems;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class EfDeliveryItemRepository(IngestorDbContext dbContext) : IDeliveryItemRepository
{
    public async Task AddRangeAsync(IReadOnlyList<DeliveryItem> items, CancellationToken ct = default)
    {
        await dbContext.DeliveryItems.AddRangeAsync(items, ct);
    }
}