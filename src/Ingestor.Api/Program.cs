using System.Text.Json;
using Ingestor.Application;
using Ingestor.Application.Telemetry;
using Ingestor.Api.Endpoints;
using Ingestor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IngestorDbContext>("database");

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

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteJsonResponse });
app.MapImportsEndpoints();
app.MapMetricsEndpoints();

app.Run();
return;

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
