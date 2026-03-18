using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.CreateImportJob;

public sealed record CreateImportJobResult(JobId JobId, bool IsNew, JobStatus? ExistingStatus = null);