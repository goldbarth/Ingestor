namespace Ingestor.Contracts.V1.Enums;

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