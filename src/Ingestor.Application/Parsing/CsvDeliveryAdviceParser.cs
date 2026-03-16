using System.Globalization;
using Ingestor.Application.Abstractions;
using Ingestor.Domain.Parsing;

namespace Ingestor.Application.Parsing;

public sealed class CsvDeliveryAdviceParser : IDeliveryAdviceParser
{
    private static readonly string[] RequiredColumns =
        [DeliveryAdviceFields.ArticleNumber, DeliveryAdviceFields.ProductName, DeliveryAdviceFields.Quantity, DeliveryAdviceFields.ExpectedDate, DeliveryAdviceFields.SupplierRef];

    public ParseResult<DeliveryAdviceLine> Parse(Stream content)
    {
        using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var headerLine = reader.ReadLine();

        if (headerLine is null)
            return ParseResult<DeliveryAdviceLine>.Failure(
            [
                new ParseError(null, "File", "File is empty")
            ]);

        var (columnIndex, readOnlyList) = ParseHeader(headerLine);
        if (readOnlyList.Count > 0)
            return ParseResult<DeliveryAdviceLine>.Failure(readOnlyList);

        var lines = new List<DeliveryAdviceLine>();
        var errors = new List<ParseError>();
        var lineNumber = 1;

        while (!reader.EndOfStream)
        {
            lineNumber++;
            var raw = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var parseErrors = TryParseLine(raw, lineNumber, columnIndex, out var line);
            if (parseErrors.Count > 0)
                errors.AddRange(parseErrors);
            else
                lines.Add(line!);
        }

        if (errors.Count > 0)
            return ParseResult<DeliveryAdviceLine>.Failure(errors);

        if (lines.Count == 0)
            return ParseResult<DeliveryAdviceLine>.Failure(
            [
                new ParseError(null, "File", "File contains no data rows")
            ]);

        return ParseResult<DeliveryAdviceLine>.Success(lines);
    }

    private static (Dictionary<string, int> columnIndex, IReadOnlyList<ParseError> errors) ParseHeader(string headerLine)
    {
        var columns = headerLine.Split(',');
        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < columns.Length; i++)
            columnIndex[columns[i].Trim()] = i;

        var errors = RequiredColumns
            .Where(col => !columnIndex.ContainsKey(col))
            .Select(col => new ParseError(null, "Header", $"Missing required column: {col}"))
            .ToList();

        return (columnIndex, errors);
    }

    private static List<ParseError> TryParseLine(
        string raw,
        int lineNumber,
        Dictionary<string, int> columnIndex,
        out DeliveryAdviceLine? line)
    {
        line = null;
        var errors = new List<ParseError>();
        var fields = raw.Split(',');

        var articleNumber = GetField(fields, columnIndex, DeliveryAdviceFields.ArticleNumber)?.Trim() ?? string.Empty;
        var productName = GetField(fields, columnIndex, DeliveryAdviceFields.ProductName)?.Trim() ?? string.Empty;
        var quantityRaw = GetField(fields, columnIndex, DeliveryAdviceFields.Quantity)?.Trim() ?? string.Empty;
        var expectedDateRaw = GetField(fields, columnIndex, DeliveryAdviceFields.ExpectedDate)?.Trim() ?? string.Empty;
        var supplierRef = GetField(fields, columnIndex, DeliveryAdviceFields.SupplierRef)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(articleNumber))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ArticleNumber, $"{DeliveryAdviceFields.ArticleNumber} is required"));

        if (string.IsNullOrWhiteSpace(productName))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ProductName, $"{DeliveryAdviceFields.ProductName} is required"));

        int quantity = 0;
        if (string.IsNullOrWhiteSpace(quantityRaw))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.Quantity, $"{DeliveryAdviceFields.Quantity} is required"));
        else if (!int.TryParse(quantityRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.Quantity, $"Value '{quantityRaw}' is not a valid integer"));

        DateTimeOffset expectedDate = default;
        if (string.IsNullOrWhiteSpace(expectedDateRaw))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ExpectedDate, $"{DeliveryAdviceFields.ExpectedDate} is required"));
        else if (!DateTimeOffset.TryParse(expectedDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expectedDate))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.ExpectedDate, $"Value '{expectedDateRaw}' is not a valid date"));

        if (string.IsNullOrWhiteSpace(supplierRef))
            errors.Add(new ParseError(lineNumber, DeliveryAdviceFields.SupplierRef, $"{DeliveryAdviceFields.SupplierRef} is required"));

        if (errors.Count > 0)
            return errors;

        line = new DeliveryAdviceLine(lineNumber, articleNumber, productName, quantity, expectedDate, supplierRef);
        return errors;
    }

    private static string? GetField(string[] fields, Dictionary<string, int> columnIndex, string column)
    {
        if (!columnIndex.TryGetValue(column, out var index) || index >= fields.Length)
            return null;

        return fields[index];
    }
}