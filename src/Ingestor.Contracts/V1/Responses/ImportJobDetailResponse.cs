namespace Ingestor.Contracts.V1.Responses;

public sealed record ImportJobDetailResponse(
    Guid Id,
    string SupplierCode,
    string ImportType,
    string Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int CurrentAttempt,
    int MaxAttempts,
    string? LastErrorCode,
    string? LastErrorMessage);