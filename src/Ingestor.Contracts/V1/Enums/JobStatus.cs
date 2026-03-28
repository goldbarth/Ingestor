namespace Ingestor.Contracts.V1.Enums;

public enum JobStatus
{
    Received           = 0,
    Parsing            = 1,
    Validating         = 2,
    Processing         = 3,
    Succeeded          = 4,
    ValidationFailed   = 5,
    ProcessingFailed   = 6,
    DeadLettered       = 7,
    PartiallySucceeded = 8
}