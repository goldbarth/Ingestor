using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ingestor.Worker;

public sealed class WorkerHeartbeatCheck(
    WorkerHeartbeat heartbeat,
    IOptions<WorkerOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var threshold = TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds * 3);
        var age = DateTimeOffset.UtcNow - heartbeat.LastBeat;

        return Task.FromResult(age <= threshold
            ? HealthCheckResult.Healthy($"Last beat {age.TotalSeconds:F1}s ago.")
            : HealthCheckResult.Unhealthy($"Worker stale. Last beat {age.TotalSeconds:F1}s ago."));
    }
}