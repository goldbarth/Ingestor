using Ingestor.Application.Common;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Application.Jobs.GetImportJobById;
using Ingestor.Application.Jobs.SearchImportJobs;
using Ingestor.Contracts.V1.Responses;
using Ingestor.Domain.Jobs;
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

        group.MapGet("{id:guid}", GetImportJobByIdAsync);
        group.MapGet("", SearchImportJobsAsync);
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

    private static async Task<IResult> GetImportJobByIdAsync(
        Guid id,
        GetImportJobByIdHandler handler,
        CancellationToken ct)
    {
        var query = new GetImportJobByIdQuery(new JobId(id));
        var result = await handler.HandleAsync(query, ct);

        if (result.IsFailure)
            return MapError(result.Error!);

        var dto = result.Value!;
        return Results.Ok(new ImportJobDetailResponse(
            dto.Id,
            dto.SupplierCode,
            dto.ImportType.ToString(),
            dto.Status.ToString(),
            dto.ReceivedAt,
            dto.StartedAt,
            dto.CompletedAt,
            dto.CurrentAttempt,
            dto.MaxAttempts,
            dto.LastErrorCode,
            dto.LastErrorMessage));
    }

    private static async Task<IResult> SearchImportJobsAsync(
        [FromQuery] string? status,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize,
        SearchImportJobsHandler handler,
        CancellationToken ct)
    {
        JobStatus? jobStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<JobStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid status.",
                    detail: $"Status '{status}' is not valid.");
            }
            jobStatus = parsedStatus;
        }

        JobId? cursorId = null;
        if (cursor is not null)
        {
            if (!Guid.TryParse(cursor, out var parsedCursor))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid cursor.",
                    detail: "Cursor must be a valid GUID.");
            }
            cursorId = new JobId(parsedCursor);
        }

        var effectivePageSize = pageSize is > 0 and <= 100 ? pageSize : 25;

        var query = new SearchImportJobsQuery(jobStatus, cursorId, effectivePageSize);
        var result = await handler.HandleAsync(query, ct);

        var page = result.Value!;
        var items = page.Items
            .Select(j => new ImportJobResponse(
                j.Id.Value,
                j.SupplierCode,
                j.ImportType.ToString(),
                j.Status.ToString(),
                j.ReceivedAt))
            .ToList();

        return Results.Ok(new CursorPagedResponse<ImportJobResponse>(
            items,
            page.NextCursor,
            page.HasNextPage));
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
