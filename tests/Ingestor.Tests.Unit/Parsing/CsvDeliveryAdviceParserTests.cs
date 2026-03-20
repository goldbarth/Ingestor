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
            ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef
            ART-001,Oak Dining Table,10,2026-04-01T00:00:00Z,SUP-42
            ART-002,Leather Sofa,5,2026-04-15T00:00:00Z,SUP-42
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().HaveCount(2);

        result.Lines[0].ArticleNumber.Should().Be("ART-001");
        result.Lines[0].ProductName.Should().Be("Oak Dining Table");
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
            ArticleNumber,ProductName,ExpectedDate,SupplierRef
            ART-001,Oak Dining Table,2026-04-01T00:00:00Z,SUP-42
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
            ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef
            ART-001,Oak Dining Table,not-a-number,2026-04-01T00:00:00Z,SUP-42
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
            ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef
            ,,not-a-number,2026-04-01T00:00:00Z,
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCount(4);
        result.Errors.Select(e => e.Field).Should().Contain(["ArticleNumber", "ProductName", "Quantity", "SupplierRef"]);
    }

    [Fact]
    public void Parse_HeaderOnlyNoDataRows_ReturnsFailureWithFileError()
    {
        var csv = "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef";

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
            ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef,UnknownColumn
            ART-001,Oak Dining Table,10,2026-04-01T00:00:00Z,SUP-42,ignored-value
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().ContainSingle()
            .Which.ArticleNumber.Should().Be("ART-001");
    }

    [Fact]
    public void Parse_InvalidUtf8Bytes_ReturnsEncodingError()
    {
        var stream = new MemoryStream(new byte[] { 0x80, 0x81, 0x82 });

        var result = _sut.Parse(stream);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Match<ParseError>(e =>
                e.Field == "File" && e.LineNumber == null && e.Message.Contains("invalid characters"));
    }

    [Fact]
    public void Parse_LargeValidFile_ParsesSuccessfully()
    {
        var header = "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\n";
        var row = "ART-001,Oak Dining Table,10,2026-04-01T00:00:00Z,SUP-42\n";
        var content = header + string.Concat(Enumerable.Repeat(row, 10_000));

        var result = _sut.Parse(ToStream(content));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().HaveCount(10_000);
    }

    [Fact]
    public void Parse_Utf8BomPreamble_ReturnsSuccessWithCorrectLines()
    {
        var csv = "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\r\n" +
                  "ART-001,Oak Dining Table,10,2026-04-01T00:00:00Z,SUP-42";

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv))
            .ToArray();

        var result = _sut.Parse(new MemoryStream(bytes));

        result.IsSuccess.Should().BeTrue();
        result.Lines.Should().ContainSingle()
            .Which.ArticleNumber.Should().Be("ART-001");
    }

    [Fact]
    public void Parse_DateOnlyExpectedDate_ParsedAsUtcMidnight()
    {
        var csv = """
            ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef
            ART-001,Oak Dining Table,10,2026-04-01,SUP-42
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeTrue();
        var expectedDate = result.Lines[0].ExpectedDate;
        expectedDate.Offset.Should().Be(TimeSpan.Zero);
        expectedDate.Should().Be(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_ExpectedDateWithExplicitOffset_ConvertedToUtc()
    {
        var csv = """
            ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef
            ART-001,Oak Dining Table,10,2026-04-01T00:00:00+02:00,SUP-42
            """;

        var result = _sut.Parse(ToStream(csv));

        result.IsSuccess.Should().BeTrue();
        var expectedDate = result.Lines[0].ExpectedDate;
        expectedDate.Offset.Should().Be(TimeSpan.Zero);
        expectedDate.Should().Be(new DateTimeOffset(2026, 3, 31, 22, 0, 0, TimeSpan.Zero));
    }
}