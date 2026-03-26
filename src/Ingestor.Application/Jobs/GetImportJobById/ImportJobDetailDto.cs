using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.GetImportJobById;

public sealed record ImportJobDetailDto(
    Guid Id,
    string SupplierCode,
    ImportType ImportType,
    JobStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int CurrentAttempt,
    int MaxAttempts,
    string? LastErrorCode,
    string? LastErrorMessage,
    bool? IsBatch,
    int? TotalLines,
    int? ProcessedLines,
    int? FailedLines,
    int? ChunkSize);
