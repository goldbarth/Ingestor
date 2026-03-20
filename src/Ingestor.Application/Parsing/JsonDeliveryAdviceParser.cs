using System.Globalization;
using System.Text.Json;
using Ingestor.Application.Abstractions;
using Ingestor.Domain.Parsing;

namespace Ingestor.Application.Parsing;

public sealed class JsonDeliveryAdviceParser : IDeliveryAdviceParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ParseResult<DeliveryAdviceLine> Parse(Stream content)
    {
        if (content is { CanSeek: true, Length: 0 })
            return ParseResult<DeliveryAdviceLine>.Failure(
            [
                new ParseError(null, "File", "File is empty")
            ]);

        List<JsonDeliveryAdviceLineDto>? dtos;

        try
        {
            dtos = JsonSerializer.Deserialize<List<JsonDeliveryAdviceLineDto>>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ParseResult<DeliveryAdviceLine>.Failure(
            [
                new ParseError(null, "File", $"Invalid JSON: {ex.Message}")
            ]);
        }

        if (dtos is null || dtos.Count == 0)
            return ParseResult<DeliveryAdviceLine>.Failure(
            [
                new ParseError(null, "File", "File contains no data rows")
            ]);

        var lines = new List<DeliveryAdviceLine>();
        var errors = new List<ParseError>();

        for (var i = 0; i < dtos.Count; i++)
        {
            var lineNumber = i + 1;
            var lineErrors = TryMapLine(dtos[i], lineNumber, out var line);

            if (lineErrors.Count > 0)
                errors.AddRange(lineErrors);
            else
                lines.Add(line!);
        }

        if (errors.Count > 0)
            return ParseResult<DeliveryAdviceLine>.Failure(errors);

        return ParseResult<DeliveryAdviceLine>.Success(lines);
    }

    private static List<ParseError> TryMapLine(JsonDeliveryAdviceLineDto dto, int lineNumber, out DeliveryAdviceLine? line)
    {
        line = null;
        var errors = new List<ParseError>();

        if (string.IsNullOrWhiteSpace(dto.ArticleNumber))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ArticleNumber, $"{DeliveryAdviceFields.ArticleNumber} is required"));

        if (string.IsNullOrWhiteSpace(dto.ProductName))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ProductName, $"{DeliveryAdviceFields.ProductName} is required"));

        if (dto.Quantity is null)
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.Quantity, $"{DeliveryAdviceFields.Quantity} is required"));

        DateTimeOffset expectedDate = default;
        if (string.IsNullOrWhiteSpace(dto.ExpectedDate))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ExpectedDate, $"{DeliveryAdviceFields.ExpectedDate} is required"));
        else if (!DateTimeOffset.TryParse(dto.ExpectedDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out expectedDate))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ExpectedDate, $"Value '{dto.ExpectedDate}' is not a valid date"));
        else
            expectedDate = expectedDate.ToUniversalTime();

        if (string.IsNullOrWhiteSpace(dto.SupplierRef))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.SupplierRef, $"{DeliveryAdviceFields.SupplierRef} is required"));

        if (errors.Count > 0)
            return errors;

        line = new DeliveryAdviceLine(lineNumber, dto.ArticleNumber!, dto.ProductName!, dto.Quantity!.Value, expectedDate, dto.SupplierRef!);
        return errors;
    }

    private sealed class JsonDeliveryAdviceLineDto
    {
        public string? ArticleNumber { get; init; }
        public string? ProductName { get; init; }
        public int? Quantity { get; init; }
        public string? ExpectedDate { get; init; }
        public string? SupplierRef { get; init; }
    }
}
