using Ingestor.Application.Jobs.CreateImportJob;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Application;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateImportJobHandler>();

        return services;
    }
}
