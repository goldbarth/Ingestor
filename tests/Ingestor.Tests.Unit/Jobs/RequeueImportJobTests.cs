using FluentAssertions;
using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Tests.Unit.Jobs;

public sealed class RequeueImportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 17, 12, 0, 0, TimeSpan.Zero);

    private static ImportJob CreateJobInStatus(JobStatus status)
    {
        var job = new ImportJob(
            JobId.New(), "SUP-01", ImportType.CsvDeliveryAdvice,
            "key", "ref", Now, maxAttempts: 3);

        switch (status)
        {
            case JobStatus.ValidationFailed:
                job.TransitionTo(JobStatus.Parsing, Now);
                job.TransitionTo(JobStatus.ValidationFailed, Now);
                break;
            case JobStatus.DeadLettered:
                job.TransitionTo(JobStatus.Parsing, Now);
                job.TransitionTo(JobStatus.Validating, Now);
                job.TransitionTo(JobStatus.Processing, Now);
                job.TransitionTo(JobStatus.ProcessingFailed, Now);
                job.RecordFailure("worker.transient_error", "timeout");
                job.TransitionTo(JobStatus.DeadLettered, Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return job;
    }

    [Theory]
    [InlineData(JobStatus.DeadLettered)]
    [InlineData(JobStatus.ValidationFailed)]
    public void Requeue_FromRequeueableStatus_SetsStatusToReceived(JobStatus from)
    {
        var job = CreateJobInStatus(from);

        job.Requeue(Now);

        job.Status.Should().Be(JobStatus.Received);
    }

    [Theory]
    [InlineData(JobStatus.DeadLettered)]
    [InlineData(JobStatus.ValidationFailed)]
    public void Requeue_ResetsAttemptCountAndErrorFields(JobStatus from)
    {
        var job = CreateJobInStatus(from);

        job.Requeue(Now);

        job.CurrentAttempt.Should().Be(0);
        job.LastErrorCode.Should().BeNull();
        job.LastErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(JobStatus.DeadLettered)]
    [InlineData(JobStatus.ValidationFailed)]
    public void Requeue_ClearsTimestamps(JobStatus from)
    {
        var job = CreateJobInStatus(from);

        job.Requeue(Now);

        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(JobStatus.Received)]
    [InlineData(JobStatus.Parsing)]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.ProcessingFailed)]
    public void Requeue_FromNonRequeueableStatus_ThrowsDomainException(JobStatus from)
    {
        var job = new ImportJob(
            JobId.New(), "SUP-01", ImportType.CsvDeliveryAdvice,
            "key", "ref", Now, maxAttempts: 3);

        // Walk to the target status via valid path
        var transitions = from switch
        {
            JobStatus.Received         => Array.Empty<JobStatus>(),
            JobStatus.Parsing          => [JobStatus.Parsing],
            JobStatus.Succeeded        => [JobStatus.Parsing, JobStatus.Validating, JobStatus.Processing, JobStatus.Succeeded],
            JobStatus.ProcessingFailed => [JobStatus.Parsing, JobStatus.Validating, JobStatus.Processing, JobStatus.ProcessingFailed],
            _ => throw new ArgumentOutOfRangeException(nameof(from))
        };

        foreach (var s in transitions)
            job.TransitionTo(s, Now);

        var act = () => job.Requeue(Now);

        act.Should().Throw<DomainException>()
            .Which.Error.Code.Should().Be("job.invalid_transition");
    }
}