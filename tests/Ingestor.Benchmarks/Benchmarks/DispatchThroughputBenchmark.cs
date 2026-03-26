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
/// Scenarios 1 and 2: N jobs dispatched sequentially, one worker processing.
/// Measures total dispatch-to-completion time and memory allocation.
///
/// NOTE: These benchmarks are designed to run on a physical developer machine,
/// not in a CI environment or VM. Results reflect real-world conditions
/// (OS scheduling, NIC latency, disk I/O) and will vary between machines.
/// This is intentional: a physical machine is the closest approximation to a
/// production deployment available without a dedicated staging environment.
/// Do not treat specific numbers as absolutes — the ordering of strategies
/// and the magnitude of differences are the meaningful signal.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class DispatchThroughputBenchmark
{
    [Params("Database", "RabbitMQ")]
    public string Strategy { get; set; } = "Database";

    [Params(100, 1000)]
    public int JobCount { get; set; } = 100;

    private static readonly byte[] CsvPayload =
        "ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef\nART-001,Sofa Milano,10,2026-04-01T00:00:00Z,REF-001\n"u8.ToArray();

    private IHost _host = null!;

    [GlobalSetup]
    public async Task GlobalSetup() =>
        _host = await BenchmarkHostBuilder.BuildAndStartAsync(Strategy);

    [IterationSetup]
    public void IterationSetup()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestorDbContext>();
        db.Database.ExecuteSqlRaw("TRUNCATE import_jobs CASCADE");
    }

    [Benchmark]
    public async Task Sequential_Throughput()
    {
        var jobIds = new List<JobId>(JobCount);

        await using var scope = _host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateImportJobHandler>();

        for (var i = 0; i < JobCount; i++)
        {
            var result = await handler.HandleAsync(
                new CreateImportJobCommand($"BENCH-{i:D6}", ImportType.CsvDeliveryAdvice, "text/csv", CsvPayload));
            jobIds.Add(result.Value!.JobId);
        }

        await BenchmarkWaiter.WaitForAllJobsAsync(_host.Services, jobIds, TimeSpan.FromMinutes(10));
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}