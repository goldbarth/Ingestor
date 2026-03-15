using FluentAssertions;
using Ingestor.Application.Parsing;
using Ingestor.Domain.Parsing;

namespace Ingestor.Tests.Unit.Parsing;

public sealed class CsvDeliveryAdviceParserTests
{
    private readonly CsvDeliveryAdviceParser _sut = new();

    private static Stream ToStream(string content)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Parse_ValidCsv_ReturnsSuccessWithAllLines()
    {
        var csv = """
            ArticleNumber,Quantity,ExpectedDate,SupplierRef
            ART-001,10,2026-04-01T00:00:00Z,SUP-42
            ART-002,5,2026-04-15T00:00:00Z,SUP-42
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().HaveCount(2);

        result.Lines[0].ArticleNumber.Should().Be("ART-001");
        result.Lines[0].Quantity.Should().Be(10);
        result.Lines[0].SupplierRef.Should().Be("SUP-42");
        result.Lines[0].LineNumber.Should().Be(2);

        result.Lines[1].ArticleNumber.Should().Be("ART-002");
        result.Lines[1].LineNumber.Should().Be(3);
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsFailureWithFileError()
    {
        var result = _sut.Parse(ToStream(string.Empty));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "File" && e.LineNumber == null);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ReturnsFailureWithHeaderError()
    {
        var csv = """
            ArticleNumber,ExpectedDate,SupplierRef
            ART-001,2026-04-01T00:00:00Z,SUP-42
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "Header" && e.Message.Contains("Quantity"));
    }

    [Fact]
    public void Parse_InvalidQuantityValue_ReturnsFailureWithLineError()
    {
        var csv = """
            ArticleNumber,Quantity,ExpectedDate,SupplierRef
            ART-001,not-a-number,2026-04-01T00:00:00Z,SUP-42
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "Quantity" && e.LineNumber == 2 && e.Message.Contains("not-a-number"));
    }

    [Fact]
    public void Parse_MultipleErrorsOnOneLine_CollectsAllErrors()
    {
        var csv = """
            ArticleNumber,Quantity,ExpectedDate,SupplierRef
            ,not-a-number,2026-04-01T00:00:00Z,
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors.Select(e => e.Field).Should().Contain(["ArticleNumber", "Quantity", "SupplierRef"]);
    }

    [Fact]
    public void Parse_HeaderOnlyNoDataRows_ReturnsFailureWithFileError()
    {
        var csv = "ArticleNumber,Quantity,ExpectedDate,SupplierRef";

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "File" && e.Message.Contains("no data rows"));
    }

    [Fact]
    public void Parse_ExtraColumns_ReturnsSuccessAndIgnoresExtraColumns()
    {
        var csv = """
            ArticleNumber,Quantity,ExpectedDate,SupplierRef,UnknownColumn
            ART-001,10,2026-04-01T00:00:00Z,SUP-42,ignored-value
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().ContainSingle()
            .Which.ArticleNumber.Should().Be("ART-001");
    }
}