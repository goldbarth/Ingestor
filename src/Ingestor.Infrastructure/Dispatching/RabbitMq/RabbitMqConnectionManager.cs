using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqConnectionManager(
    RabbitMqOptions options,
    ILogger<RabbitMqConnectionManager> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _publisherChannel;
    private IChannel? _consumerChannel;

    public async Task<IChannel> GetPublishChannelAsync(CancellationToken ct)
    {
        if (_publisherChannel is { IsOpen: true })
            return _publisherChannel;

        await _initLock.WaitAsync(ct);

        try
        {
            if (_publisherChannel is { IsOpen: true })
                return _publisherChannel;

            await EnsureConnectionAsync(ct);
            _publisherChannel = await _connection!.CreateChannelAsync(cancellationToken: ct);
            await DeclareTopologyAsync(_publisherChannel, ct);
            return _publisherChannel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IChannel> GetConsumerChannelAsync(CancellationToken ct)
    {
        if (_consumerChannel is { IsOpen: true })
            return _consumerChannel;

        await _initLock.WaitAsync(ct);

        try
        {
            if (_consumerChannel is { IsOpen: true })
                return _consumerChannel;

            await EnsureConnectionAsync(ct);
            _consumerChannel = await _connection!.CreateChannelAsync(cancellationToken: ct);
            await DeclareTopologyAsync(_consumerChannel, ct);
            return _consumerChannel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return;

        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password
        };

        _connection = await factory.CreateConnectionAsync(ct);

        _connection.ConnectionShutdownAsync += (_, args) =>
        {
            logger.LogWarning("RabbitMQ connection lost: {Reason}.", args.ReplyText);
            return Task.CompletedTask;
        };

        _connection.RecoverySucceededAsync += (_, _) =>
        {
            logger.LogInformation("RabbitMQ connection recovered.");
            return Task.CompletedTask;
        };
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: options.DeadLetterExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: options.DeadLetterQueueName,
            exchange: options.DeadLetterExchangeName,
            routingKey: string.Empty,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = options.DeadLetterExchangeName },
            cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_publisherChannel is not null) await _publisherChannel.DisposeAsync();
        if (_consumerChannel is not null) await _consumerChannel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}