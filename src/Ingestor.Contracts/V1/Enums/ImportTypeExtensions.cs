namespace Ingestor.Contracts.V1.Enums;

public static class ImportTypeExtensions
{
    public static string ToMediaType(this ImportType importType) => importType switch
    {
        ImportType.CsvDeliveryAdvice  => "text/csv",
        ImportType.JsonDeliveryAdvice => "application/json",
        _ => throw new ArgumentOutOfRangeException(nameof(importType), importType, null)
    };
}
