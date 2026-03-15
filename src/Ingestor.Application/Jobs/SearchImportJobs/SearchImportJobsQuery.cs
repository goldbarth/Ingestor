using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.SearchImportJobs;

public sealed record SearchImportJobsQuery(
    JobStatus? Status,
    JobId? Cursor,
    int PageSize);
