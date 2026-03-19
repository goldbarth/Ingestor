using Ingestor.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ingestor.Infrastructure;

namespace Ingestor.Infrastructure.Persistence;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        services.AddDbContext<IngestorDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            configureOptions?.Invoke(options);
        });

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