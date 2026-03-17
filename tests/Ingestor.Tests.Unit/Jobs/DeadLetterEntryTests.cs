using FluentAssertions;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Tests.Unit.Jobs;

public sealed class DeadLetterEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 17, 12, 0, 0, TimeSpan.Zero);

    private static ImportJob CreateFailedJob(int maxAttempts = 3)
    {
        var job = new ImportJob(
            JobId.New(),
            "SUP-01",
            ImportType.CsvDeliveryAdvice,
            "idempotency-key",
            "payload-ref",
            Now,
            maxAttempts);

        job.TransitionTo(JobStatus.Parsing, Now);
        job.TransitionTo(JobStatus.Validating, Now);
        job.TransitionTo(JobStatus.Processing, Now);
        job.TransitionTo(JobStatus.ProcessingFailed, Now);
        job.RecordFailure("worker.transient_error", "Connection timed out.");

        return job;
    }

    [Fact]
    public void From_SetsJobId()
    {
        var job = CreateFailedJob();
        var deadLetteredAt = Now.AddSeconds(1);

        var entry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, deadLetteredAt);

        entry.JobId.Should().Be(job.Id);
    }

    [Fact]
    public void From_SetsReasonFromLastErrorCode()
    {
        var job = CreateFailedJob();

        var entry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, Now);

        entry.Reason.Should().Be("worker.transient_error");
    }

    [Fact]
    public void From_SetsErrorMessageFromLastErrorMessage()
    {
        var job = CreateFailedJob();

        var entry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, Now);

        entry.ErrorMessage.Should().Be("Connection timed out.");
    }

    [Fact]
    public void From_SetsSnapshotFields()
    {
        var job = CreateFailedJob(maxAttempts: 3);

        var entry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, Now);

        entry.SupplierCode.Should().Be("SUP-01");
        entry.ImportType.Should().Be(ImportType.CsvDeliveryAdvice);
        entry.TotalAttempts.Should().Be(job.CurrentAttempt);
    }

    [Fact]
    public void From_SetsDeadLetteredAt()
    {
        var job = CreateFailedJob();
        var deadLetteredAt = Now.AddMinutes(5);

        var entry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, deadLetteredAt);

        entry.DeadLetteredAt.Should().Be(deadLetteredAt);
    }

    [Fact]
    public void From_WhenLastErrorCodeIsNull_UsesUnknownAsReason()
    {
        var job = new ImportJob(
            JobId.New(), "SUP-01", ImportType.CsvDeliveryAdvice,
            "key", "ref", Now, maxAttempts: 1);

        // No RecordFailure call → LastErrorCode remains null
        job.TransitionTo(JobStatus.Parsing, Now);
        job.TransitionTo(JobStatus.Validating, Now);
        job.TransitionTo(JobStatus.Processing, Now);
        job.TransitionTo(JobStatus.ProcessingFailed, Now);

        var entry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, Now);

        entry.Reason.Should().Be("unknown");
    }
}
