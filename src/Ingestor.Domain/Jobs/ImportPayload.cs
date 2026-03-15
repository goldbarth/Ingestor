namespace Ingestor.Domain.Jobs;

public sealed class ImportPayload
{
    public PayloadId Id { get; private set; }
    public Guid JobId { get; private set; }
    public string ContentType { get; private set; }
    public byte[] RawData { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    
#pragma warning disable CS8618
    private ImportPayload() {}
#pragma warning restore CS8618

    public ImportPayload(PayloadId id, Guid jobId, string contentType, byte[] rawData, DateTimeOffset receivedAt)
    {
        Id = id;
        JobId = jobId;
        ContentType = contentType;
        RawData = rawData;
        SizeBytes = rawData.Length;
        ReceivedAt = receivedAt;
    }
}