using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;
using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.RequeueImportJob;

public sealed class RequeueImportJobHandler(
    IImportJobRepository jobRepository,
    IOutboxRepository outboxRepository,
    IAuditEventRepository auditEventRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly HashSet<JobStatus> _requeueableStatuses =
    [
        JobStatus.DeadLettered,
        JobStatus.ValidationFailed
    ];

    public async Task<Result<RequeueImportJobResult>> HandleAsync(
        RequeueImportJobCommand command, CancellationToken ct = default)
    {
        var job = await jobRepository.GetByIdAsync(command.Id, ct);

        if (job is null)
            return Result<RequeueImportJobResult>.NotFound(
                "job.not_found",
                $"Import job '{command.Id.Value}' was not found.");

        if (!_requeueableStatuses.Contains(job.Status))
            return Result<RequeueImportJobResult>.Conflict(
                "job.not_requeueable",
                $"Import job '{command.Id.Value}' cannot be requeued from status '{job.Status}'.");

        var now = clock.UtcNow;
        var oldStatus = job.Status;

        job.Requeue(now);

        await auditEventRepository.AddAsync(new AuditEvent(
            AuditEventId.New(), job.Id, oldStatus, JobStatus.Received,
            AuditEventTrigger.Api, now), ct);

        var outboxEntry = new OutboxEntry(OutboxEntryId.New(), job.Id, now, attemptNumber: 1);
        await outboxRepository.AddAsync(outboxEntry, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<RequeueImportJobResult>.Success(new RequeueImportJobResult(job.Id.Value));
    }
}