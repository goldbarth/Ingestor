using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Processing;

public sealed record ProcessDeliveryItemsResult
{
    public bool IsSuccess { get; init; }
    public int ProcessedItemCount { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static ProcessDeliveryItemsResult Success(int processedItemCount) => new()
    {
        IsSuccess = true,
        ProcessedItemCount = processedItemCount
    };

    public static ProcessDeliveryItemsResult JobNotFound(JobId jobId) => new()
    {
        IsSuccess = false,
        ErrorCode = "processing.job_not_found",
        ErrorMessage = $"ImportJob '{jobId.Value}' was not found"
    };
}
