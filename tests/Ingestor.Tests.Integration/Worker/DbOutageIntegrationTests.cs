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
    private static readonly byte[] ValidCsvBytes =
        "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\nART-001,Sofa Milano,10,2026-04-01T00:00:00Z,REF-001\n"u8.ToArray();

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
}
