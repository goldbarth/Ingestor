using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Domain.Jobs;

public sealed class ImportAttempt
{
    public ImportAttemptId Id { get; private set; }
    public JobId JobId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public AttemptOutcome Outcome { get; private set; }
    public ErrorCategory? ErrorCategory { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public long DurationMs { get; private set; }

#pragma warning disable CS8618
    private ImportAttempt() { }
#pragma warning restore CS8618

    public static ImportAttempt Succeeded(
        ImportAttemptId id,
        JobId jobId,
        int attemptNumber,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt) => new()
    {
        Id = id,
        JobId = jobId,
        AttemptNumber = attemptNumber,
        StartedAt = startedAt,
        FinishedAt = finishedAt,
        Outcome = AttemptOutcome.Succeeded,
        DurationMs = (long)(finishedAt - startedAt).TotalMilliseconds
    };

    public static ImportAttempt Failed(
        ImportAttemptId id,
        JobId jobId,
        int attemptNumber,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        ErrorCategory errorCategory,
        string errorCode,
        string errorMessage) => new()
    {
        Id = id,
        JobId = jobId,
        AttemptNumber = attemptNumber,
        StartedAt = startedAt,
        FinishedAt = finishedAt,
        Outcome = AttemptOutcome.Failed,
        ErrorCategory = errorCategory,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        DurationMs = (long)(finishedAt - startedAt).TotalMilliseconds
    };
}