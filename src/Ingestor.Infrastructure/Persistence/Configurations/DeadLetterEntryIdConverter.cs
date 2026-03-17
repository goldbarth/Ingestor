using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class DeadLetterEntryIdConverter() : ValueConverter<DeadLetterEntryId, Guid>(
    id => id.Value,
    value => new DeadLetterEntryId(value));