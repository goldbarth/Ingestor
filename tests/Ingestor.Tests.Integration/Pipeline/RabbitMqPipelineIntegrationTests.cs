using FluentAssertions;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Tests.Integration.Pipeline;

public sealed class RabbitMqPipelineIntegrationTests(RabbitMqFixture fixture)
    : IClassFixture<RabbitMqFixture>
{
    private static readonly byte[] ValidCsvBytes =
        "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\nART-001,Sofa Milano,10,2026-04-01T00:00:00Z,REF-001\nART-002,Chair Stockholm,5,2026-04-15T00:00:00Z,REF-002\n"u8.ToArray();

    [Fact]
    public async Task FullPipeline_ValidJob_DispatchedViaRabbitMq_PersistsDeliveryItemsAndSucceeds()
    {
        // Arrange: create and dispatch job — RabbitMqJobDispatcher publishes to the broker
        await using var arrangeScope = fixture.Services.CreateAsyncScope();
        var handler = arrangeScope.ServiceProvider.GetRequiredService<CreateImportJobHandler>();
        var command = new CreateImportJobCommand("SUP-RABBIT", ImportType.CsvDeliveryAdvice, "text/csv", ValidCsvBytes);
        var result = await handler.HandleAsync(command);
        var jobId = result.Value!.JobId;

        // Act: RabbitMqWorker picks up the message asynchronously — poll until terminal status
        var job = await WaitUntilAsync(
            async () =>
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                return await scope.ServiceProvider
                    .GetRequiredService<IImportJobRepository>()
                    .GetByIdAsync(jobId);
            },
            j => j?.Status is JobStatus.Succeeded or JobStatus.ValidationFailed or JobStatus.DeadLettered,
            timeout: TimeSpan.FromSeconds(15));

        // Assert
        job.Should().NotBeNull("worker should have processed the job within the timeout");
        job!.Status.Should().Be(JobStatus.Succeeded);

        await using var assertScope = fixture.Services.CreateAsyncScope();
        var items = await assertScope.ServiceProvider
            .GetRequiredService<IngestorDbContext>()
            .DeliveryItems.Where(d => d.JobId == jobId).ToListAsync();

        items.Should().HaveCount(2);
    }

    private static async Task<T?> WaitUntilAsync<T>(
        Func<Task<T?>> query,
        Func<T?, bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var value = await query();
            if (condition(value))
                return value;
            await Task.Delay(interval);
        }

        return await query();
    }
}