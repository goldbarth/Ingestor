using FluentAssertions;
using Ingestor.Domain.Common;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Tests.Unit.Jobs;

public sealed class ImportJobTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    private static ImportJob CreateJob(JobStatus status)
    {
        // Build a job and walk it to the desired status via valid transitions,
        // so we always start from a consistent, realistic state.
        var job = new ImportJob(
            JobId.New(),
            "SUP-01",
            ImportType.CsvDeliveryAdvice,
            "idempotency-key",
            "payload-ref",
            Now,
            maxAttempts: 3);

        // Walk through the state machine to reach the requested starting status.
        var path = ResolvePathTo(status);
        foreach (var next in path)
            job.TransitionTo(next, Now);

        return job;
    }

    // Minimal paths to reach each status from Received.
    private static IEnumerable<JobStatus> ResolvePathTo(JobStatus target) => target switch
    {
        JobStatus.Received         => [],
        JobStatus.Parsing          => [JobStatus.Parsing],
        JobStatus.Validating       => [JobStatus.Parsing, JobStatus.Validating],
        JobStatus.Processing       => [JobStatus.Parsing, JobStatus.Validating, JobStatus.Processing],
        JobStatus.Succeeded        => [JobStatus.Parsing, JobStatus.Validating, JobStatus.Processing, JobStatus.Succeeded],
        JobStatus.ValidationFailed => [JobStatus.Parsing, JobStatus.ValidationFailed],
        JobStatus.ProcessingFailed => [JobStatus.Parsing, JobStatus.Validating, JobStatus.Processing, JobStatus.ProcessingFailed],
        JobStatus.DeadLettered     => [JobStatus.Parsing, JobStatus.Validating, JobStatus.Processing, JobStatus.ProcessingFailed, JobStatus.DeadLettered],
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    // --- Valid transitions ---

    [Theory]
    [InlineData(JobStatus.Received,         JobStatus.Parsing)]
    [InlineData(JobStatus.Parsing,          JobStatus.Validating)]
    [InlineData(JobStatus.Parsing,          JobStatus.ProcessingFailed)]
    [InlineData(JobStatus.Parsing,          JobStatus.ValidationFailed)]
    [InlineData(JobStatus.Validating,       JobStatus.Processing)]
    [InlineData(JobStatus.Validating,       JobStatus.ValidationFailed)]
    [InlineData(JobStatus.Processing,       JobStatus.Succeeded)]
    [InlineData(JobStatus.Processing,       JobStatus.ProcessingFailed)]
    [InlineData(JobStatus.ProcessingFailed, JobStatus.Parsing)]
    [InlineData(JobStatus.ProcessingFailed, JobStatus.DeadLettered)]
    [InlineData(JobStatus.DeadLettered,     JobStatus.Received)]
    public void TransitionTo_ValidTransition_SetsStatus(JobStatus from, JobStatus to)
    {
        var job = CreateJob(from);

        job.TransitionTo(to, Now);

        job.Status.Should().Be(to);
    }

    // --- Timestamp rules ---

    [Fact]
    public void TransitionTo_Parsing_SetsStartedAt()
    {
        var job = CreateJob(JobStatus.Received);

        job.TransitionTo(JobStatus.Parsing, Now);

        job.StartedAt.Should().Be(Now);
    }

    [Fact]
    public void TransitionTo_ParsingOnRetry_DoesNotOverwriteStartedAt()
    {
        var earlier = Now.AddMinutes(-5);
        var job = CreateJob(JobStatus.Received);
        job.TransitionTo(JobStatus.Parsing, earlier);
        job.TransitionTo(JobStatus.Validating, earlier);
        job.TransitionTo(JobStatus.Processing, earlier);
        job.TransitionTo(JobStatus.ProcessingFailed, earlier);

        job.TransitionTo(JobStatus.Parsing, Now);

        job.StartedAt.Should().Be(earlier);
    }

    [Theory]
    [InlineData(JobStatus.Processing,       JobStatus.Succeeded)]
    [InlineData(JobStatus.Parsing,          JobStatus.ValidationFailed)]
    [InlineData(JobStatus.ProcessingFailed, JobStatus.DeadLettered)]
    public void TransitionTo_TerminalStatus_SetsCompletedAt(JobStatus from, JobStatus to)
    {
        var job = CreateJob(from);

        job.TransitionTo(to, Now);

        job.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void TransitionTo_ProcessingFailed_DoesNotSetCompletedAt()
    {
        var job = CreateJob(JobStatus.Processing);

        job.TransitionTo(JobStatus.ProcessingFailed, Now);

        job.CompletedAt.Should().BeNull();
    }

    // --- Invalid transitions ---

    [Theory]
    [InlineData(JobStatus.Received,   JobStatus.Succeeded)]
    [InlineData(JobStatus.Succeeded,  JobStatus.Parsing)]
    [InlineData(JobStatus.Validating, JobStatus.Received)]
    public void TransitionTo_InvalidTransition_ThrowsDomainException(JobStatus from, JobStatus to)
    {
        var job = CreateJob(from);

        var act = () => job.TransitionTo(to, Now);

        act.Should().Throw<DomainException>()
            .Which.Error.Code.Should().Be("job.invalid_transition");
    }
}
