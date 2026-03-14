namespace Ingestor.Contracts.V1.Responses;

public sealed record ImportJobResponse(
    Guid Id,
    string SupplierCode,
    string ImportType,
    string Status,
    DateTimeOffset ReceivedAt);