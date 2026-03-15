using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class ImportPayloadIdConverter() : ValueConverter<PayloadId, Guid>(
    id => id.Value, 
    value => new PayloadId(value));