namespace Ingestor.Domain.DeliveryItems;

public readonly record struct DeliveryItemId(Guid Value)
{
    public static DeliveryItemId New() => new(Guid.CreateVersion7());
}
