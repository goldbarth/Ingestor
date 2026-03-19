using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventIdConverter() : ValueConverter<AuditEventId, Guid>(
    id => id.Value,
    value => new AuditEventId(value));