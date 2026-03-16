namespace Ingestor.Domain.Parsing;

public sealed record DeliveryAdviceLine(
    int LineNumber,
    string ArticleNumber,
    string ProductName,
    int Quantity,
    DateTimeOffset ExpectedDate,
    string SupplierRef);