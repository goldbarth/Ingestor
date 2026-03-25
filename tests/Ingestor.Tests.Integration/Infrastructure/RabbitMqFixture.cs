using Ingestor.Application;
using Ingestor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Ingestor.Tests.Integration.Infrastructure;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("ingestor_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IHost _host = null!;

    public IServiceProvider Services => _host.Services;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dispatch:Strategy"]   = "RabbitMQ",
            ["RabbitMQ:Host"]       = _rabbitMq.Hostname,
            ["RabbitMQ:Port"]       = _rabbitMq.GetMappedPublicPort(5672).ToString(),
            ["RabbitMQ:UserName"]   = "test",
            ["RabbitMQ:Password"]   = "test"
        });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration, _postgres.GetConnectionString());

        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IngestorDbContext>()
                .Database.MigrateAsync();
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await Task.WhenAll(_postgres.StopAsync(), _rabbitMq.StopAsync());
    }
}