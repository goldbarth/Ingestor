using BenchmarkDotNet.Attributes;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Benchmarks.Infrastructure;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ingestor.Benchmarks.Benchmarks;

/// <summary>
/// Scenario 3: 100 jobs with 2 concurrent workers processing in parallel.
/// Both workers share the same DB and RabbitMQ queue.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class ConcurrentThroughputBenchmark
{
    private const int JobCount = 100;

    [Params("Database", "RabbitMQ")]
    public string Strategy { get; set; } = "Database";

    private static readonly byte[] CsvPayload =
        "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\nART-001,Sofa Milano,10,2026-04-01T00:00:00Z,REF-001\n"u8.ToArray();

    private IHost _worker1 = null!;
    private IHost _worker2 = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _worker1 = await BenchmarkHostBuilder.BuildAndStartAsync(Strategy);
        _worker2 = await BenchmarkHostBuilder.BuildAndStartAsync(Strategy);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var scope = _worker1.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestorDbContext>();
        db.Database.ExecuteSqlRaw("TRUNCATE import_jobs CASCADE");
    }

    [Benchmark]
    public async Task Concurrent_2Workers_100Jobs()
    {
        var jobIds = new List<JobId>(JobCount);

        await using var scope = _worker1.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateImportJobHandler>();

        for (var i = 0; i < JobCount; i++)
        {
            var result = await handler.HandleAsync(
                new CreateImportJobCommand($"BENCH-{i:D6}", ImportType.CsvDeliveryAdvice, "text/csv", CsvPayload));
            jobIds.Add(result.Value!.JobId);
        }

        await BenchmarkWaiter.WaitForAllJobsAsync(_worker1.Services, jobIds, TimeSpan.FromMinutes(10));
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await Task.WhenAll(_worker1.StopAsync(), _worker2.StopAsync());
        _worker1.Dispose();
        _worker2.Dispose();
    }
}