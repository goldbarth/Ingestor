namespace Ingestor.Application.Common;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public ApplicationError? Error { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private Result(ApplicationError error)
    {
        IsSuccess = false;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Conflict(string code, string message) =>
        new(new ApplicationError(code, message, ErrorType.Conflict));

    public static Result<T> NotFound(string code, string message) =>
        new(new ApplicationError(code, message, ErrorType.NotFound));

    public static Result<T> Validation(string code, string message) =>
        new(new ApplicationError(code, message, ErrorType.Validation));
}
