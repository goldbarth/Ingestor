namespace Ingestor.Application.Jobs.SearchImportJobs;

public sealed record SearchImportJobsResult(
    IReadOnlyList<ImportJobSummaryDto> Items,
    string? NextCursor,
    bool HasNextPage);
