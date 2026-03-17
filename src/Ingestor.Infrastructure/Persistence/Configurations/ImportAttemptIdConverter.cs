using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class ImportAttemptIdConverter() : ValueConverter<ImportAttemptId, Guid>(
    id => id.Value,
    value => new ImportAttemptId(value));