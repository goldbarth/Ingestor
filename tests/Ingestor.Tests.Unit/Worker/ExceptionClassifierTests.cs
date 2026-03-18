using FluentAssertions;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Infrastructure;

namespace Ingestor.Tests.Unit.Worker;

public sealed class ExceptionClassifierTests
{
    private readonly ExceptionClassifier _sut = new();

    [Fact]
    public void Classify_TimeoutException_ReturnsTransient()
    {
        var result = _sut.Classify(new TimeoutException());

        result.Should().Be(ErrorCategory.Transient);
    }

    [Fact]
    public void Classify_ArgumentException_ReturnsPermanent()
    {
        var result = _sut.Classify(new ArgumentException("bad argument"));

        result.Should().Be(ErrorCategory.Permanent);
    }

    [Fact]
    public void Classify_InvalidOperationException_ReturnsPermanent()
    {
        var result = _sut.Classify(new InvalidOperationException("unexpected state"));

        result.Should().Be(ErrorCategory.Permanent);
    }

    [Fact]
    public void Classify_FormatException_ReturnsPermanent()
    {
        var result = _sut.Classify(new FormatException("malformed input"));

        result.Should().Be(ErrorCategory.Permanent);
    }
}