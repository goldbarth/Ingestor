using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class ImportJobIdConverter() : ValueConverter<JobId, Guid>(
    id => id.Value,
    value => new JobId(value));