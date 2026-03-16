using Ingestor.Domain.DeliveryItems;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryItemIdConverter() : ValueConverter<DeliveryItemId, Guid>(
    id => id.Value,
    value => new DeliveryItemId(value));