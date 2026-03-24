using Ingestor.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ingestor.Infrastructure.Dispatching;
using Ingestor.Infrastructure.Dispatching.RabbitMq;
using Microsoft.Extensions.Configuration;

namespace Ingestor.Infrastructure.Persistence;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        services.AddDbContext<IngestorDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            configureOptions?.Invoke(options);
        });
        
        var strategy = configuration["Dispatch:Strategy"] ?? "Database";
        
        if (strategy.Equals(nameof(DispatchStrategy.RabbitMQ), StringComparison.OrdinalIgnoreCase))
        {
            var rabbitMqOptions = new RabbitMqOptions();
            configuration.GetSection(RabbitMqOptions.SectionName).Bind(rabbitMqOptions);
            services.AddSingleton(rabbitMqOptions);
            services.AddSingleton<RabbitMqConnectionManager>();
            services.AddSingleton<RabbitMqDeliveryTagStore>();
            services.AddScoped<IJobDispatcher, RabbitMqJobDispatcher>();
        }
        else
            services.AddScoped<IJobDispatcher, DatabaseJobDispatcher>();

        services.AddScoped<IImportJobRepository, ImportJobRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IDeliveryItemRepository, EfDeliveryItemRepository>();
        services.AddScoped<IImportAttemptRepository, EfImportAttemptRepository>();
        services.AddScoped<IDeadLetterRepository, EfDeadLetterRepository>();
        services.AddScoped<IAuditEventRepository, EfAuditEventRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IExceptionClassifier, ExceptionClassifier>();

        return services;
    }
}