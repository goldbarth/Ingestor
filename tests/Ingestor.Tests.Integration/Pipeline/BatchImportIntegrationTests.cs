using System.Text;
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

public sealed class BatchImportIntegrationTests(FaultInjectablePostgreSqlFixture fixture)
    : IClassFixture<FaultInjectablePostgreSqlFixture>
{
    private const int LineCount = 10_000;
    private const int ChunkSize = 500;                   // BatchOptions default
    private const int ExpectedChunks = LineCount / ChunkSize; // 20

    // Generated once per test-class load — fast (~ms) and shared across all tests.
    private static readonly byte[] TenThousandValidLines = GenerateValidCsv(LineCount);

    // Line 251 falls inside chunk 1 (lines 1–500); Quantity=0 triggers the validator.
    private static readonly byte[] TenThousandLinesWithOneInvalid =
        GenerateCsvWithInvalidLine(LineCount, invalidAtLine: 251);

    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_TenThousandValidLines_SucceedsWithCorrectBatchTracking()
    {
        // Arrange
        var jobId = await CreateJobAsync(TenThousandValidLines, "SUP-BATCH-LARGE");

        // Act
        await using var actScope = fixture.Services.CreateAsyncScope();
        var result = await actScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);

        // Assert – pipeline result
        result.IsSuccess.Should().BeTrue();
        result.ProcessedItemCount.Should().Be(LineCount);

        // Assert – job state and batch-tracking fields
        await using var assertScope = fixture.Services.CreateAsyncScope();
        var job = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);

        job!.Status.Should().Be(JobStatus.Succeeded);
        job.IsBatch.Should().BeTrue();
        job.TotalLines.Should().Be(LineCount);
        job.ChunkSize.Should().Be(ChunkSize);
        job.ProcessedLines.Should().Be(LineCount);
        job.FailedLines.Should().Be(0);

        // Assert – every line produced a persisted DeliveryItem
        var items = await assertScope.ServiceProvider
            .GetRequiredService<IngestorDbContext>()
            .DeliveryItems.Where(d => d.JobId == jobId).ToListAsync();

        items.Should().HaveCount(LineCount);
    }

    [Fact]
    public async Task HandleAsync_TenThousandValidLines_WithChunkProcessingFault_PartiallySucceeds()
    {
        // Arrange
        var jobId = await CreateJobAsync(TenThousandValidLines, "SUP-BATCH-PARTIAL");

        // Schedule fault on the 4th SaveChangesAsync call inside HandleAsync:
        //   #1 Parsing transition, #2 Validating transition, #3 Processing transition,
        //   #4 Chunk 1 save  ← fault fires here → chunk 1 is recorded as failed.
        fixture.FaultInterceptor.TriggerFaultAfter(3);

        // Act
        await using var actScope = fixture.Services.CreateAsyncScope();
        var result = await actScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);

        // Assert – the pipeline treats a partial completion as success from the caller's view
        result.IsSuccess.Should().BeTrue();

        // Assert – job reflects partial success
        await using var assertScope = fixture.Services.CreateAsyncScope();
        var job = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);

        job!.Status.Should().Be(JobStatus.PartiallySucceeded);
        job.IsBatch.Should().BeTrue();
        job.TotalLines.Should().Be(LineCount);
        job.FailedLines.Should().Be(ChunkSize);    // one chunk (500 lines) reported as failed
        job.FailedLines.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_TenThousandLines_InvalidQuantityInChunk_TransitionsToValidationFailed()
    {
        // Arrange – line 251 has Quantity=0; validator rule: Quantity must be > 0.
        //           Chunk 1 covers lines 1–500, so the first chunk fails validation,
        //           which aborts the entire job before any items are persisted.
        var jobId = await CreateJobAsync(TenThousandLinesWithOneInvalid, "SUP-BATCH-INVALID");

        // Act
        await using var actScope = fixture.Services.CreateAsyncScope();
        var result = await actScope.ServiceProvider
            .GetRequiredService<ImportPipelineHandler>()
            .HandleAsync(jobId);

        // Assert – pipeline reports validation failure
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("pipeline.validation_failed");

        // Assert – job transitions to ValidationFailed; no items persisted
        await using var assertScope = fixture.Services.CreateAsyncScope();
        var job = await assertScope.ServiceProvider
            .GetRequiredService<IImportJobRepository>()
            .GetByIdAsync(jobId);

        job!.Status.Should().Be(JobStatus.ValidationFailed);
        job.LastErrorCode.Should().Be("pipeline.validation_failed");

        var items = await assertScope.ServiceProvider
            .GetRequiredService<IngestorDbContext>()
            .DeliveryItems.Where(d => d.JobId == jobId).ToListAsync();

        items.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------

    private async Task<JobId> CreateJobAsync(byte[] payload, string supplierCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateImportJobHandler>();
        var command = new CreateImportJobCommand(supplierCode, ImportType.CsvDeliveryAdvice, "text/csv", payload);
        var result = await handler.HandleAsync(command);
        return result.Value!.JobId;
    }

    private static byte[] GenerateValidCsv(int lineCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef");
        for (var i = 1; i <= lineCount; i++)
            sb.AppendLine($"ART-{i:D6},Product {i},{i % 100 + 1},2027-01-01T00:00:00Z,REF-{i:D6}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] GenerateCsvWithInvalidLine(int lineCount, int invalidAtLine)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef");
        for (var i = 1; i <= lineCount; i++)
        {
            var qty = i == invalidAtLine ? 0 : i % 100 + 1; // Quantity=0 fails the validator
            sb.AppendLine($"ART-{i:D6},Product {i},{qty},2027-01-01T00:00:00Z,REF-{i:D6}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
