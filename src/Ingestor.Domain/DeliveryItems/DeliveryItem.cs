using Ingestor.Domain.Jobs;

namespace Ingestor.Domain.DeliveryItems;

public sealed class DeliveryItem
{
    public DeliveryItemId Id { get; private set; }
    public JobId JobId { get; private set; }
    public string ArticleNumber { get; private set; }
    public string ProductName { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset ExpectedDate { get; private set; }
    public string SupplierRef { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

#pragma warning disable CS8618
    private DeliveryItem() { }
#pragma warning restore CS8618

    public DeliveryItem(
        DeliveryItemId id,
        JobId jobId,
        string articleNumber,
        string productName,
        int quantity,
        DateTimeOffset expectedDate,
        string supplierRef,
        DateTimeOffset processedAt)
    {
        Id = id;
        JobId = jobId;
        ArticleNumber = articleNumber;
        ProductName = productName;
        Quantity = quantity;
        ExpectedDate = expectedDate;
        SupplierRef = supplierRef;
        ProcessedAt = processedAt;
    }
}