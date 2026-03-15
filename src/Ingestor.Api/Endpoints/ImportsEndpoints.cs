using Ingestor.Application.Common;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Contracts.V1.Responses;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Ingestor.Api.Endpoints;

public static class ImportsEndpoints
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "text/csv",
        "application/json"
    ];

    public static void MapImportsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/imports");

        group.MapPost("", UploadImportAsync)
            .DisableAntiforgery();
    }

    private static async Task<IResult> UploadImportAsync(
        [FromForm] string supplierCode,
        [FromForm] string importType,
        IFormFile file,
        CreateImportJobHandler handler,
        CancellationToken ct)
    {
        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid content type.",
                detail: $"Content type '{file.ContentType}' is not supported. Allowed: text/csv, application/json.");
        }

        if (!Enum.TryParse<ImportType>(importType, ignoreCase: true, out var domainImportType))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid import type.",
                detail: $"Import type '{importType}' is not valid.");
        }

        using var stream = file.OpenReadStream();
        var rawData = new byte[file.Length];
        await stream.ReadExactlyAsync(rawData, ct);

        var command = new CreateImportJobCommand(
            supplierCode,
            domainImportType,
            file.ContentType,
            rawData);

        var result = await handler.HandleAsync(command, ct);

        if (result.IsFailure)
            return MapError(result.Error!);

        return Results.Created(
            $"/api/imports/{result.Value!.Value}",
            new ImportJobResponse(
                result.Value.Value,
                supplierCode,
                importType,
                "Received",
                DateTimeOffset.UtcNow));
    }

    private static IResult MapError(ApplicationError error) => error.Type switch
    {
        ErrorType.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict.",
            detail: error.Message),
        _ => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.")
    };
}
