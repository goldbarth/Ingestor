using Ingestor.Application;
using Ingestor.Infrastructure.Persistence;
using WorkerNs = Ingestor.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ingestor.Benchmarks.Infrastructure;

internal static class BenchmarkHostBuilder
{
    public static async Task<IHost> BuildAndStartAsync(string strategy)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        builder.Configuration["Dispatch:Strategy"] = strategy;

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required. " +
                "Set it via the ConnectionStrings__DefaultConnection environment variable.");

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration, connectionString);

        if (!strategy.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<WorkerNs.WorkerHeartbeat>();
            builder.Services.Configure<WorkerNs.WorkerOptions>(builder.Configuration.GetSection(WorkerNs.WorkerOptions.SectionName));
            builder.Services.AddHostedService<WorkerNs.Worker>();
        }

        var host = builder.Build();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IngestorDbContext>()
                .Database.MigrateAsync();
        }

        await host.StartAsync();
        return host;
    }
}