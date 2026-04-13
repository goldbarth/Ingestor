using System.Text;
using FluentAssertions;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Tests.Integration.Worker;

public sealed class DbOutageIntegrationTests(FaultInjectablePostgreSqlFixture fixture)
    : IClassFixture<FaultInjectablePostgreSqlFixture>
{
    private static byte[] ValidCsvBytes =>
        Encoding.UTF8.GetBytes(
            "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\n" +
            $"ART-001,Sofa Milano,10,{DateTimeOffset.UtcNow.AddDays(30):yyyy-MM-dd}T00:00:00Z,REF-001\n");

    private async Task<Domain.Jobs.JobId> CreateJobAsync(string supplierCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateImportJobHandler>();
        var command = new CreateImportJobCommand(supplierCode, ImportType.CsvDeliveryAdvice, "text/csv", ValidCsvBytes);
        var result = await handler.HandleAsync(command);
        return result.Value!.JobId;
    }

    [Fact]
    public async Task ClaimNextAsync_DbTimesOutDuringSave_OutboxEntryRemainsInPendingState()
    {
        // Arrange
        var jobId = await CreateJobAsync("SUP-OUTAGE-A");

        // Inject fault: the next SaveChangesAsync (inside ClaimNextAsync) will throw
        fixture.FaultInterceptor.ShouldFail = true;

        // Act
        await using var failScope = fixture.Services.CreateAsyncScope();
        var outboxRepo = failScope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var act = () => outboxRepo.ClaimNextAsync();
        await act.Should().ThrowAsync<TimeoutException>("a DB timeout was injected into the next SaveChangesAsync");

        // Assert: EF Core rolled back the transaction — OutboxEntry must still be Pending
        await using var assertScope = fixture.Services.CreateAsyncScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IngestorDbContext>();
        var entry = await db.OutboxEntries.FirstAsync(e => e.JobId == jobId);
        entry.Status.Should().Be(OutboxStatus.Pending,
            "a failed claim must not leave the outbox entry in a non-recoverable state");
    }

    [Fact]
    public async Task Pipeline_DbTimesOutAfterClaim_JobStatusRemainsInReceived()
    {
        // Arrange: create job and successfully claim the outbox entry
        var jobId = await CreateJobAsync("SUP-OUTAGE-B");

        await using var claimScope = fixture.Services.CreateAsyncScope();
        var claimedEntry = await claimScope.ServiceProvider
            .GetRequiredService<IOutboxRepository>()
            .ClaimNextAsync();
        claimedEntry.Should().NotBeNull();

        // Inject fault: the pipeline's first SaveChangesAsync (Parsing transition) will throw
        fixture.FaultInterceptor.ShouldFail = true;

        // Act: run the pipeline — it throws before any state transition is committed
        await using var pipelineScope = fixture.Services.CreateAsyncScope();
        var pipeline = pipelineScope.ServiceProvider.GetRequiredService<ImportPipelineHandler>();
        var act = () => pipeline.HandleAsync(jobId);
        await act.Should().ThrowAsync<TimeoutException>("a DB timeout was injected into the first pipeline save");

        // Assert: job is still Received — the failed save was fully rolled back
        await using var assertScope = fixture.Services.CreateAsyncScope();
        var job = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);
        var outboxEntry = await assertScope.ServiceProvider
            .GetRequiredService<IngestorDbContext>()
            .OutboxEntries.FirstAsync(e => e.JobId == jobId);

        job!.Status.Should().Be(JobStatus.Received,
            "no pipeline state should be persisted when SaveChangesAsync throws");
        outboxEntry.Status.Should().Be(OutboxStatus.Processing,
            "the outbox entry was claimed before the fault and is now stuck in Processing — " +
            "this documents the known architectural gap: without a stale-lock timeout, " +
            "a worker crash after claim leaves the entry non-recoverable");
    }

    [Fact]
    public async Task StaleOutboxEntry_AfterDbTimeoutDuringPipeline_IsRecoveredToPendingAndReprocessed()
    {
        // Arrange: create job and successfully claim its outbox entry
        var jobId = await CreateJobAsync("SUP-STALE");

        await using var claimScope = fixture.Services.CreateAsyncScope();
        var claimedEntry = await claimScope.ServiceProvider
            .GetRequiredService<IOutboxRepository>()
            .ClaimNextAsync();
        claimedEntry.Should().NotBeNull();

        // Simulate a DB timeout during the pipeline → entry is now stuck in Processing
        fixture.FaultInterceptor.ShouldFail = true;
        await using var pipelineScope = fixture.Services.CreateAsyncScope();
        var act = () => pipelineScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);
        await act.Should().ThrowAsync<TimeoutException>();

        // Act: recover stale entries — timeout=0 means any Processing entry qualifies immediately
        await using var recoverScope = fixture.Services.CreateAsyncScope();
        var recovered = await recoverScope.ServiceProvider
            .GetRequiredService<IOutboxRepository>()
            .RecoverStaleAsync(TimeSpan.Zero);

        // Assert: at least our entry was recovered (shared fixture may have other stale entries)
        recovered.Should().BeGreaterThanOrEqualTo(1);

        await using var checkScope = fixture.Services.CreateAsyncScope();
        var db = checkScope.ServiceProvider.GetRequiredService<IngestorDbContext>();
        var entry = await db.OutboxEntries.FirstAsync(e => e.JobId == jobId);
        entry.Status.Should().Be(OutboxStatus.Pending, "recovery must reset the status to Pending");
        entry.LockedAt.Should().BeNull("recovery must clear the stale lock timestamp");

        // Act: process the recovered job — no fault injected
        await using var reprocessScope = fixture.Services.CreateAsyncScope();
        var result = await reprocessScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);
        result.IsSuccess.Should().BeTrue();

        // Assert: job completed successfully end-to-end
        await using var finalScope = fixture.Services.CreateAsyncScope();
        var finalJob = await finalScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);
        finalJob!.Status.Should().Be(JobStatus.Succeeded);
    }
}
