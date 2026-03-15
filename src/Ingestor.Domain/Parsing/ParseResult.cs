namespace Ingestor.Domain.Parsing;

public sealed class ParseResult<T>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<T> Lines { get; }
    public IReadOnlyList<ParseError> Errors { get; }

    private ParseResult(bool isSuccess, IReadOnlyList<T> lines, IReadOnlyList<ParseError> errors)
    {
        IsSuccess = isSuccess;
        Lines = lines;
        Errors = errors;
    }

    public static ParseResult<T> Success(IReadOnlyList<T> lines)
        => new(true, lines, []);

    public static ParseResult<T> Failure(IReadOnlyList<ParseError> errors)
        => new(false, [], errors);
}