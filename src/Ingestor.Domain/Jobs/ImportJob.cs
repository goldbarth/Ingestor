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

    public void TransitionTo(JobStatus newStatus, DateTimeOffset now, int? processedItemCount = null)
    {
        ImportJobWorkflow.EnsureCanTransition(Status, newStatus);

        if (newStatus == JobStatus.Parsing && StartedAt is null)
            StartedAt = now;

        if (newStatus is JobStatus.Succeeded or JobStatus.ValidationFailed or JobStatus.DeadLettered)
            CompletedAt = now;

        if (newStatus == JobStatus.Succeeded && processedItemCount.HasValue)
            ProcessedItemCount = processedItemCount.Value;

        Status = newStatus;
    }
}