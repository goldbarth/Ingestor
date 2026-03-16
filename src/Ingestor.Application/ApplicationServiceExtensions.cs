using Ingestor.Application.Abstractions;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Application.Jobs.GetImportJobById;
using Ingestor.Application.Jobs.SearchImportJobs;
using Ingestor.Application.Parsing;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Application;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateImportJobHandler>();
        services.AddScoped<GetImportJobByIdHandler>();
        services.AddScoped<SearchImportJobsHandler>();

        services.AddSingleton<IDeliveryAdviceParser, CsvDeliveryAdviceParser>();

        return services;
    }
}
