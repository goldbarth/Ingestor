namespace Ingestor.Application.Common;

public sealed record ApplicationError(string Code, string Message, ErrorType Type);
