using System.Text.Json;
using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;
using RabbitMQ.Client;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqJobDispatcher(
    RabbitMqConnectionManager connectionManager,
    RabbitMqDeliveryTagStore deliveryTagStore,
    RabbitMqOptions options) : IJobDispatcher
{
    public async Task DispatchAsync(ImportJob job, CancellationToken ct = default)
    {
        var channel = await connectionManager.GetPublishChannelAsync(ct);
        
        var message = new ImportJobMessage(job.Id.Value, job.SupplierCode, job.ImportType.ToString());
        
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.QueueName,
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: body,
            cancellationToken: ct);
    }

    public async Task AcknowledgeAsync(ImportJob job, CancellationToken ct = default)
    {
        if (!deliveryTagStore.TryRemove(job.Id, out var deliveryTag))
            return;
        
        var channel = await connectionManager.GetConsumerChannelAsync(ct);

        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
    }
}

internal sealed record ImportJobMessage(Guid JobId, string SupplierCode, string ImportType);