using FluentAssertions;
using Ingestor.Application.Parsing;
using Ingestor.Domain.Parsing;

namespace Ingestor.Tests.Unit.Parsing;

public sealed class JsonDeliveryAdviceParserTests
{
    private readonly JsonDeliveryAdviceParser _sut = new();

    private static Stream ToStream(string content)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Parse_ValidJson_ReturnsSuccessWithAllLines()
    {
        var json = """
            [
              { "articleNumber": "ART-001", "productName": "Oak Dining Table", "quantity": 10, "expectedDate": "2026-04-01T00:00:00Z", "supplierRef": "SUP-42" },
              { "articleNumber": "ART-002", "productName": "Leather Sofa",     "quantity": 5,  "expectedDate": "2026-04-15T00:00:00Z", "supplierRef": "SUP-42" }
            ]
            """;

        var result = _sut.Parse(ToStream(json));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().HaveCount(2);

        result.Lines[0].ArticleNumber.Should().Be("ART-001");
        result.Lines[0].ProductName.Should().Be("Oak Dining Table");
        result.Lines[0].Quantity.Should().Be(10);
        result.Lines[0].SupplierRef.Should().Be("SUP-42");
        result.Lines[0].LineNumber.Should().Be(1);

        result.Lines[1].ArticleNumber.Should().Be("ART-002");
        result.Lines[1].LineNumber.Should().Be(2);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsFailureWithFileError()
    {
        var result = _sut.Parse(ToStream("this is not json"));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "File" && e.LineNumber == null && e.Message.Contains("Invalid JSON"));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsFailureWithFileError()
    {
        var result = _sut.Parse(ToStream("[]"));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "File" && e.Message.Contains("no data rows"));
    }

    [Fact]
    public void Parse_MissingRequiredField_ReturnsFailureWithLineError()
    {
        var json = """
            [
              { "productName": "Oak Dining Table", "quantity": 10, "expectedDate": "2026-04-01T00:00:00Z", "supplierRef": "SUP-42" }
            ]
            """;

        var result = _sut.Parse(ToStream(json));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "ArticleNumber" && e.LineNumber == 1);
    }

    [Fact]
    public void Parse_WrongTypeForQuantity_ReturnsFailureWithFileError()
    {
        var json = """
            [
              { "articleNumber": "ART-001", "productName": "Oak Dining Table", "quantity": "not-a-number", "expectedDate": "2026-04-01T00:00:00Z", "supplierRef": "SUP-42" }
            ]
            """;

        var result = _sut.Parse(ToStream(json));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "File" && e.LineNumber == null);
    }

    [Fact]
    public void Parse_MultipleErrorsOnOneLine_CollectsAllErrors()
    {
        var json = """
            [
              { "quantity": 10, "expectedDate": "2026-04-01T00:00:00Z" }
            ]
            """;

        var result = _sut.Parse(ToStream(json));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors.Select(e => e.Field).Should().Contain(["ArticleNumber", "ProductName", "SupplierRef"]);
    }
}