using FluentAssertions;
using Ingestor.Domain.Common;
using Ingestor.Domain.Parsing;
using Ingestor.Domain.Validation;

namespace Ingestor.Tests.Unit.Validation;

public sealed class DeliveryAdviceValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly DeliveryAdviceValidator _sut = new(new FakeClock(Now));

    private static DeliveryAdviceLine ValidLine(int lineNumber = 1) =>
        new(lineNumber, "ART-001", "Oak Dining Table", 10, Now.AddDays(1), "SUP-42");

    [Fact]
    public void Validate_ValidLines_ReturnsSuccess()
    {
        var result = _sut.Validate([ValidLine(1), ValidLine(2)]);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyArticleNumber_ReturnsValidationError()
    {
        var line = ValidLine() with { ArticleNumber = "" };

        var result = _sut.Validate([line]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ValidationError>(e =>
                e.Field == "ArticleNumber" && e.LineNumber == 1);
    }

    [Fact]
    public void Validate_ZeroQuantity_ReturnsValidationError()
    {
        var line = ValidLine() with { Quantity = 0 };

        var result = _sut.Validate([line]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ValidationError>(e =>
                e.Field == "Quantity" && e.LineNumber == 1);
    }

    [Fact]
    public void Validate_NegativeQuantity_ReturnsValidationError()
    {
        var line = ValidLine() with { Quantity = -1 };

        var result = _sut.Validate([line]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ValidationError>(e =>
                e.Field == "Quantity" && e.LineNumber == 1);
    }

    [Fact]
    public void Validate_ExpectedDateInThePast_ReturnsValidationError()
    {
        var line = ValidLine() with { ExpectedDate = Now.AddDays(-1) };

        var result = _sut.Validate([line]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ValidationError>(e =>
                e.Field == "ExpectedDate" && e.LineNumber == 1);
    }

    [Fact]
    public void Validate_EmptySupplierRef_ReturnsValidationError()
    {
        var line = ValidLine() with { SupplierRef = "" };

        var result = _sut.Validate([line]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ValidationError>(e =>
                e.Field == "SupplierRef" && e.LineNumber == 1);
    }

    [Fact]
    public void Validate_MultipleErrorsOnOneLine_CollectsAllErrors()
    {
        var line = ValidLine() with { ArticleNumber = "", Quantity = 0 };

        var result = _sut.Validate([line]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Select(e => e.Field).Should().Contain(["ArticleNumber", "Quantity"]);
    }

    [Fact]
    public void Validate_ErrorsAcrossMultipleLines_CollectsAllErrors()
    {
        var lines = new[]
        {
            ValidLine(1) with { Quantity = 0 },
            ValidLine(2) with { SupplierRef = "" }
        };

        var result = _sut.Validate(lines);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.LineNumber == 1 && e.Field == "Quantity");
        result.Errors.Should().Contain(e => e.LineNumber == 2 && e.Field == "SupplierRef");
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
