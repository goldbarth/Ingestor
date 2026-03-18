using Ingestor.Application;
using Ingestor.Application.Telemetry;
using Ingestor.Api.Endpoints;
using Ingestor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(IngestorActivitySource.Name)
        .AddConsoleExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IngestorDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

app.MapImportsEndpoints();

app.Run();
