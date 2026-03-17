namespace Ingestor.Application.Pipeline;

public sealed record PipelineResult
{
    public bool IsSuccess { get; init; }
    public int ProcessedItemCount { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static PipelineResult Success(int processedItemCount) => new()
    {
        IsSuccess = true,
        ProcessedItemCount = processedItemCount
    };

    public static PipelineResult Failed(string errorCode, string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}