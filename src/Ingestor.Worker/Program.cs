using System.Text.Json;
using Ingestor.Application;
using Ingestor.Application.Telemetry;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Infrastructure.Telemetry;
using Ingestor.Worker;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((_, config) =>
    config.ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddInfrastructure(builder.Configuration ,connectionString);
builder.Services.AddApplication();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(builder.Environment.ApplicationName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(IngestorMeter.Name)
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddSource(IngestorActivitySource.Name)
        .AddSource(IngestorDatabaseActivitySource.Name)
        .AddSource(IngestorMessagingActivitySource.Name)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithLogging(logging => logging
            .AddOtlpExporter(),
        options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
        });

builder.Services.AddSingleton<WorkerHeartbeat>();

var dispatchStrategy = builder.Configuration["Dispatch:Strategy"] ?? "Database";
if (!dispatchStrategy.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
    builder.Services.AddHostedService<Worker>();
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IngestorDbContext>("database")
    .AddCheck<WorkerHeartbeatCheck>("worker-heartbeat");

var app = builder.Build();

var rawStrategy = app.Configuration["Dispatch:Strategy"];
if (!string.IsNullOrWhiteSpace(rawStrategy)
    && !rawStrategy.Equals("Database", StringComparison.OrdinalIgnoreCase)
    && !rawStrategy.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogWarning("Dispatch:Strategy '{Strategy}' is unknown. Falling back to 'Database'.", rawStrategy);
}

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteJsonResponse });

app.Run();

static Task WriteJsonResponse(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    var result = JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        results = report.Entries.ToDictionary(
            e => e.Key,
            e => new { status = e.Value.Status.ToString(), description = e.Value.Description })
    });
    return ctx.Response.WriteAsync(result);
}
