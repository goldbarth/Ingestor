using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;

namespace Ingestor.Application.Jobs.GetJobHistory;

public sealed class GetJobHistoryHandler(
    IImportJobRepository jobRepository,
    IAuditEventRepository auditEventRepository)
{
    public async Task<Result<IReadOnlyList<AuditEventDto>>> HandleAsync(
        GetJobHistoryQuery query, CancellationToken ct = default)
    {
        var job = await jobRepository.GetByIdAsync(query.JobId, ct);

        if (job is null)
            return Result<IReadOnlyList<AuditEventDto>>.NotFound(
                "job.not_found",
                $"Import job '{query.JobId.Value}' was not found.");

        var events = await auditEventRepository.GetByJobIdAsync(query.JobId, ct);

        var dtos = events
            .Select(e => new AuditEventDto(e.OccurredAt, e.OldStatus, e.NewStatus, e.TriggeredBy, e.Comment))
            .ToList();

        return Result<IReadOnlyList<AuditEventDto>>.Success(dtos);
    }
}