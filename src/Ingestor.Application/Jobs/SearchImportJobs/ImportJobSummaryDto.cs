using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.SearchImportJobs;

public sealed record ImportJobSummaryDto(
    JobId Id,
    string SupplierCode,
    ImportType ImportType,
    JobStatus Status,
    DateTimeOffset ReceivedAt);
