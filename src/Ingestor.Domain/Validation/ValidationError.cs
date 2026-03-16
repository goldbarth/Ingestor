namespace Ingestor.Domain.Validation;

public sealed record ValidationError(
    int LineNumber,
    string Field,
    string Message);