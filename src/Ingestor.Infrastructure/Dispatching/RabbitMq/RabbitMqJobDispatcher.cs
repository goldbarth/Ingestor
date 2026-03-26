using System.Text.Json;
using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs;
using Ingestor.Infrastructure.Persistence;
using Ingestor.Infrastructure.Telemetry;
using RabbitMQ.Client;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqJobDispatcher(
    RabbitMqConnectionManager connectionManager,
    RabbitMqDeliveryTagStore deliveryTagStore,
    RabbitMqOptions options,
    IAfterSaveCallbackRegistry afterSaveCallbackRegistry) : IJobDispatcher
{
    public Task DispatchAsync(ImportJob job, CancellationToken ct = default)
    {
        afterSaveCallbackRegistry.OnAfterSave(async token =>
        {
            var channel = await connectionManager.GetPublishChannelAsync(token);
            var message = new ImportJobMessage(job.Id.Value, job.SupplierCode, job.ImportType.ToString());
            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            using var activity = RabbitMqTelemetry.StartProducerActivity(
                IngestorMessagingActivitySource.Messaging,
                options,
                job.Id.Value,
                body.Length);
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: options.QueueName,
                mandatory: false,
                basicProperties: RabbitMqTelemetry.CreateBasicProperties(activity, job.Id.Value),
                body: body,
                cancellationToken: token);
        });
        return Task.CompletedTask;
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
