using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;

namespace Ingestor.Application.Jobs.SearchImportJobs;

public sealed class SearchImportJobsHandler(IImportJobRepository jobRepository)
{
    public async Task<Result<SearchImportJobsResult>> HandleAsync(
        SearchImportJobsQuery query, CancellationToken ct = default)
    {
        var results = await jobRepository.SearchAsync(
            query.Status,
            query.Cursor,
            query.PageSize + 1,
            ct);

        var hasNextPage = results.Count > query.PageSize;
        var items = hasNextPage ? results.Take(query.PageSize).ToList() : results;

        var dtos = items.Select(j => new ImportJobSummaryDto(
            j.Id,
            j.SupplierCode,
            j.ImportType,
            j.Status,
            j.ReceivedAt)).ToList();

        var nextCursor = hasNextPage ? items[^1].Id.Value.ToString() : null;

        return Result<SearchImportJobsResult>.Success(
            new SearchImportJobsResult(dtos, nextCursor, hasNextPage));
    }
}
