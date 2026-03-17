using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Domain.Jobs;

public sealed class DeadLetterEntry
{
    public DeadLetterEntryId Id { get; private set; }
    public JobId JobId { get; private set; }
    public string Reason { get; private set; }
    public string ErrorMessage { get; private set; }
    public string SupplierCode { get; private set; }
    public ImportType ImportType { get; private set; }
    public int TotalAttempts { get; private set; }
    public DateTimeOffset DeadLetteredAt { get; private set; }

#pragma warning disable CS8618
    private DeadLetterEntry() { }
#pragma warning restore CS8618

    public static DeadLetterEntry From(DeadLetterEntryId id, ImportJob job, DateTimeOffset deadLetteredAt) => new()
    {
        Id = id,
        JobId = job.Id,
        Reason = job.LastErrorCode ?? "unknown",
        ErrorMessage = job.LastErrorMessage ?? string.Empty,
        SupplierCode = job.SupplierCode,
        ImportType = job.ImportType,
        TotalAttempts = job.CurrentAttempt,
        DeadLetteredAt = deadLetteredAt
    };
}
