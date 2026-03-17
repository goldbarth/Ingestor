using Ingestor.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Infrastructure.Persistence;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IngestorDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IImportJobRepository, ImportJobRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IDeliveryItemRepository, EfDeliveryItemRepository>();
        services.AddScoped<IImportAttemptRepository, EfImportAttemptRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }
}