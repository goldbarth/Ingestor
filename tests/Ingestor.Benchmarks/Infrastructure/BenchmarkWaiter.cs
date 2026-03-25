using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Benchmarks.Infrastructure;

internal static class BenchmarkWaiter
{
    public static async Task WaitForAllJobsAsync(
        IServiceProvider services,
        IReadOnlyList<JobId> jobIds,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var pending = new HashSet<Guid>(jobIds.Select(j => j.Value));

        while (pending.Count > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();

            foreach (var id in pending.ToList())
            {
                var job = await repo.GetByIdAsync(new JobId(id));
                if (job?.Status is JobStatus.Succeeded or JobStatus.ValidationFailed or JobStatus.DeadLettered)
                    pending.Remove(id);
            }

            if (pending.Count > 0)
                await Task.Delay(50);
        }

        if (pending.Count > 0)
            throw new TimeoutException(
                $"{pending.Count}/{jobIds.Count} jobs did not reach a terminal status within {timeout}.");
    }
}