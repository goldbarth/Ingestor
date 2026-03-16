namespace Ingestor.Domain.Validation;

public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    private ValidationResult(bool isValid, IReadOnlyList<ValidationError> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public static ValidationResult Success()
        => new(true, []);

    public static ValidationResult Failure(IReadOnlyList<ValidationError> errors)
        => new(false, errors);
}