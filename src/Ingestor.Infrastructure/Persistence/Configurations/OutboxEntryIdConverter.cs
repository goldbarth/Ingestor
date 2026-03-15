using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ingestor.Domain.Jobs;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEntryIdConverter() : ValueConverter<OutboxEntryId, Guid>(
    id => id.Value,
    value => new OutboxEntryId(value));
