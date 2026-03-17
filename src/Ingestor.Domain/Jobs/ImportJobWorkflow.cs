using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Domain.Jobs;

public static class ImportJobWorkflow
{
    private static readonly HashSet<(JobStatus From, JobStatus To)> _allowed =
    [
        (JobStatus.Received,         JobStatus.Parsing),
        (JobStatus.Parsing,          JobStatus.Validating),
        (JobStatus.Parsing,          JobStatus.ProcessingFailed),
        (JobStatus.Parsing,          JobStatus.ValidationFailed),
        (JobStatus.Validating,       JobStatus.Processing),
        (JobStatus.Validating,       JobStatus.ValidationFailed),
        (JobStatus.Processing,       JobStatus.Succeeded),
        (JobStatus.Processing,       JobStatus.ProcessingFailed),
        (JobStatus.ProcessingFailed, JobStatus.Parsing),
        (JobStatus.ProcessingFailed, JobStatus.DeadLettered),
        (JobStatus.DeadLettered,     JobStatus.Received),      // Manual requeue
        (JobStatus.ValidationFailed, JobStatus.Received),      // Manual requeue after correction
    ];

    public static bool CanTransition(JobStatus from, JobStatus to)
        => _allowed.Contains((from, to));

    public static void EnsureCanTransition(JobStatus from, JobStatus to)
    {
        if (!CanTransition(from, to))
            throw new DomainException(new DomainError(
                "job.invalid_transition",
                $"Transition from '{from}' to '{to}' is not allowed."));
    }
}