namespace Ingestor.Domain.Parsing;

public sealed record DeliveryAdviceLine(
    int LineNumber,
    string ArticleNumber,
    decimal Quantity,
    DateTimeOffset ExpectedDate,
    string SupplierRef);