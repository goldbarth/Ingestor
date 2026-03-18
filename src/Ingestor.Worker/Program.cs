using System.Text.Json;
using Ingestor.Application;
using Ingestor.Application.Telemetry;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Worker;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((_, config) =>
    config.ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(IngestorActivitySource.Name)
        .AddConsoleExporter());

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddSingleton<WorkerHeartbeat>();
builder.Services.AddHostedService<Worker>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IngestorDbContext>("database")
    .AddCheck<WorkerHeartbeatCheck>("worker-heartbeat");

var app = builder.Build();

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