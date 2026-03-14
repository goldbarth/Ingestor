namespace Ingestor.Domain.Jobs;

public enum JobStatus
{
    Received,
    Parsing,
    Validating,
    Processing,
    Succeeded,
    ValidationFailed,
    ProcessingFailed,
    DeadLettered
}