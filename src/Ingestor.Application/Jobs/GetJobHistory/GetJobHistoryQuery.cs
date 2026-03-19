using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Jobs.GetJobHistory;

public sealed record GetJobHistoryQuery(JobId JobId);
