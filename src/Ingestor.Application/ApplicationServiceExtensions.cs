using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Application.Jobs.GetImportJobById;
using Ingestor.Application.Jobs.SearchImportJobs;
using Ingestor.Application.Parsing;
using Ingestor.Domain.Common;
using Ingestor.Domain.Validation;
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
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<DeliveryAdviceValidator>();

        return services;
    }
}
