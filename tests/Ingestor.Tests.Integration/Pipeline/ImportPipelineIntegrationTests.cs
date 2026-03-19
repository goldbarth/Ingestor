using FluentAssertions;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Tests.Integration.Pipeline;

public sealed class ImportPipelineIntegrationTests(PostgreSqlFixture fixture)
    : IClassFixture<PostgreSqlFixture>
{
    private static readonly byte[] ValidCsvBytes =
        "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\nART-001,Sofa Milano,10,2026-04-01T00:00:00Z,REF-001\nART-002,Chair Stockholm,5,2026-04-15T00:00:00Z,REF-002\n"u8.ToArray();

    private static readonly byte[] InvalidCsvBytes =
        "ArticleNumber,ProductName\nART-001,Sofa Milano\n"u8.ToArray();

    private async Task<JobId> CreateJobAsync(byte[] payload, string supplierCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateImportJobHandler>();
        var command = new CreateImportJobCommand(supplierCode, ImportType.CsvDeliveryAdvice, "text/csv", payload);
        var result = await handler.HandleAsync(command);
        return result.Value!.JobId;
    }

    [Fact]
    public async Task HandleAsync_ValidCsvJob_PersistsDeliveryItemsAndSucceeds()
    {
        // Arrange
        var jobId = await CreateJobAsync(ValidCsvBytes, "SUP-HAPPY");

        // Act
        await using var actScope = fixture.Services.CreateAsyncScope();
        var result = await actScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ProcessedItemCount.Should().Be(2);

        await using var assertScope = fixture.Services.CreateAsyncScope();
        var job = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);
        var items = await assertScope.ServiceProvider
            .GetRequiredService<IngestorDbContext>()
            .DeliveryItems.Where(d => d.JobId == jobId).ToListAsync();

        job!.Status.Should().Be(JobStatus.Succeeded);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandleAsync_InvalidCsvJob_TransitionsToValidationFailed()
    {
        // Arrange
        var jobId = await CreateJobAsync(InvalidCsvBytes, "SUP-INVALID");

        // Act
        await using var actScope = fixture.Services.CreateAsyncScope();
        var result = await actScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("pipeline.parse_failed");

        await using var assertScope = fixture.Services.CreateAsyncScope();
        var job = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);

        job!.Status.Should().Be(JobStatus.ValidationFailed);
    }

    [Fact]
    public async Task HandleAsync_JobAfterTransientFailure_RecoversThroughRetry()
    {
        // Arrange: create job
        var jobId = await CreateJobAsync(ValidCsvBytes, "SUP-RETRY");

        // Simulate prior transient failure: walk job to ProcessingFailed
        await using var failScope = fixture.Services.CreateAsyncScope();
        var jobRepo = failScope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var uow = failScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;
        var job = await jobRepo.GetByIdAsync(jobId);
        job!.TransitionTo(JobStatus.Parsing, now);
        job.TransitionTo(JobStatus.Validating, now);
        job.TransitionTo(JobStatus.Processing, now);
        job.TransitionTo(JobStatus.ProcessingFailed, now);
        job.RecordFailure("worker.transient_error", "Simulated timeout");
        await uow.SaveChangesAsync();

        // Act: retry — pipeline runs ProcessingFailed → Parsing → ... → Succeeded
        await using var retryScope = fixture.Services.CreateAsyncScope();
        var result = await retryScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await using var assertScope = fixture.Services.CreateAsyncScope();
        var savedJob = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);

        savedJob!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task RecordFailure_ExhaustsMaxAttempts_TransitionsToDeadLettered()
    {
        // Arrange: create job directly with MaxAttempts = 1
        var jobId = JobId.New();
        var now = DateTimeOffset.UtcNow;

        await using var arrangeScope = fixture.Services.CreateAsyncScope();
        var jobRepo = arrangeScope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var deadLetterRepo = arrangeScope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var uow = arrangeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var payload = new ImportPayload(PayloadId.New(), jobId, "text/csv", ValidCsvBytes, now);
        var job = new ImportJob(jobId, "SUP-DEAD", ImportType.CsvDeliveryAdvice,
            Guid.NewGuid().ToString(), payload.Id.Value.ToString(), now, maxAttempts: 1);

        await jobRepo.AddAsync(job, payload);

        // Walk to ProcessingFailed and exhaust attempts
        job.TransitionTo(JobStatus.Parsing, now);
        job.TransitionTo(JobStatus.Validating, now);
        job.TransitionTo(JobStatus.Processing, now);
        job.TransitionTo(JobStatus.ProcessingFailed, now);
        job.RecordFailure("worker.transient_error", "Connection timeout");

        // Act: dead-letter (CurrentAttempt == MaxAttempts)
        var deadLetterEntry = DeadLetterEntry.From(DeadLetterEntryId.New(), job, now);
        await deadLetterRepo.AddAsync(deadLetterEntry);
        job.TransitionTo(JobStatus.DeadLettered, now);
        await uow.SaveChangesAsync();

        // Assert
        await using var assertScope = fixture.Services.CreateAsyncScope();
        var savedJob = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);
        var dlEntry = await assertScope.ServiceProvider
            .GetRequiredService<IngestorDbContext>()
            .DeadLetterEntries.FirstAsync(d => d.JobId == jobId);

        savedJob!.Status.Should().Be(JobStatus.DeadLettered);
        dlEntry.TotalAttempts.Should().Be(1);
        dlEntry.Reason.Should().Be("worker.transient_error");
        dlEntry.SupplierCode.Should().Be("SUP-DEAD");
    }
}