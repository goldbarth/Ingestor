namespace Ingestor.Domain.Jobs.Enums;

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