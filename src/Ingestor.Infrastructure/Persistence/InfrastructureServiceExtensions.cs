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

        return services;
    }
}