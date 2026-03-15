namespace Ingestor.Domain.Parsing;

public sealed record ParseError(
    int? LineNumber,
    string Field,
    string Message);