using Ingestor.Domain.Jobs;

namespace Ingestor.Application.Jobs.RequeueImportJob;

public sealed record RequeueImportJobCommand(JobId Id);