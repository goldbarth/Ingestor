using Ingestor.Application;
using Ingestor.Application.Telemetry;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Worker;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(IngestorActivitySource.Name)
        .AddConsoleExporter());
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();