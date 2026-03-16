using Ingestor.Domain.Common;
using Ingestor.Domain.Parsing;

namespace Ingestor.Domain.Validation;

public sealed class DeliveryAdviceValidator(IClock clock)
{
    public ValidationResult Validate(IReadOnlyList<DeliveryAdviceLine> lines)
    {
        var errors = new List<ValidationError>();

        foreach (var line in lines)
            errors.AddRange(ValidateLine(line));

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success();
    }

    private List<ValidationError> ValidateLine(DeliveryAdviceLine line)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(line.ArticleNumber))
            errors.Add(new ValidationError(line.LineNumber, nameof(line.ArticleNumber), $"{nameof(line.ArticleNumber)} is required"));

        if (string.IsNullOrWhiteSpace(line.ProductName))
            errors.Add(new ValidationError(line.LineNumber, nameof(line.ProductName), $"{nameof(line.ProductName)} is required"));

        if (line.Quantity <= 0)
            errors.Add(new ValidationError(line.LineNumber, nameof(line.Quantity), $"{nameof(line.Quantity)} must be greater than 0, got {line.Quantity}"));

        if (line.ExpectedDate <= clock.UtcNow)
            errors.Add(new ValidationError(line.LineNumber, nameof(line.ExpectedDate), $"{nameof(line.ExpectedDate)} must be in the future"));

        if (string.IsNullOrWhiteSpace(line.SupplierRef))
            errors.Add(new ValidationError(line.LineNumber, nameof(line.SupplierRef), $"{nameof(line.SupplierRef)} is required"));

        return errors;
    }
}