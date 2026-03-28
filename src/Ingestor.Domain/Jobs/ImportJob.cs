using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Domain.Jobs;

public sealed class ImportJob
{
    public JobId Id { get; private set; }
    public string SupplierCode { get; private set; }
    public ImportType ImportType { get; private set; }
    public JobStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayloadReference { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int CurrentAttempt { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public int ProcessedItemCount { get; private set; }

    public bool? IsBatch { get; private set; }
    public int? TotalLines { get; private set; }
    public int? ProcessedLines { get; private set; }
    public int? FailedLines { get; private set; }
    public int? ChunkSize { get; private set; }

#pragma warning disable CS8618
    private ImportJob() {}
#pragma warning restore CS8618

    public ImportJob(JobId id, string supplierCode, ImportType importType,
        string idempotencyKey, string payloadReference, DateTimeOffset receivedAt,
        int maxAttempts)
    {
        Id = id;
        SupplierCode = supplierCode;
        ImportType = importType;
        IdempotencyKey = idempotencyKey;
        PayloadReference = payloadReference;
        ReceivedAt = receivedAt;
        CurrentAttempt = 0;
        MaxAttempts = maxAttempts;
        Status = JobStatus.Received;
    }

    public void RecordFailure(string errorCode, string errorMessage)
    {
        CurrentAttempt++;
        LastErrorCode = errorCode;
        LastErrorMessage = errorMessage;
    }

    public void RecordPermanentFailure(string errorCode, string errorMessage)
    {
        LastErrorCode = errorCode;
        LastErrorMessage = errorMessage;
    }

    public void InitializeBatch(int totalLines, int chunkSize)
    {
        IsBatch = true;
        TotalLines = totalLines;
        ChunkSize = chunkSize;
        ProcessedLines = 0;
        FailedLines = 0;
    }

    public void RecordChunkProcessed(int count)
    {
        ProcessedLines = (ProcessedLines ?? 0) + count;
    }

    public void Requeue(DateTimeOffset now)
    {
        ImportJobWorkflow.EnsureCanTransition(Status, JobStatus.Received);

        CurrentAttempt = 0;
        StartedAt = null;
        CompletedAt = null;
        LastErrorCode = null;
        LastErrorMessage = null;
        Status = JobStatus.Received;
    }

    public void TransitionTo(JobStatus newStatus, DateTimeOffset now, int? processedItemCount = null)
    {
        ImportJobWorkflow.EnsureCanTransition(Status, newStatus);

        if (newStatus == JobStatus.PartiallySucceeded && IsBatch != true)
            throw new DomainException(new DomainError(
                "job.invalid_status_for_non_batch",
                "PartiallySucceeded is only valid for batch jobs."));

        if (newStatus == JobStatus.Parsing && StartedAt is null)
            StartedAt = now;

        if (newStatus is JobStatus.Succeeded or JobStatus.PartiallySucceeded or JobStatus.ValidationFailed or JobStatus.DeadLettered)
            CompletedAt = now;

        if ((newStatus is JobStatus.Succeeded or JobStatus.PartiallySucceeded) && processedItemCount.HasValue)
            ProcessedItemCount = processedItemCount.Value;

        Status = newStatus;
    }
}