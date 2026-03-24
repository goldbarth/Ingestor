using RabbitMQ.Client;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqConnectionManager(RabbitMqOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _publisherChannel;
    private IChannel? _consumerChannel;
    
    public async Task<IChannel> GetPublishChannelAsync(CancellationToken ct)
    {
        if (_publisherChannel is {IsOpen: true})
            return _publisherChannel;
        
        await _initLock.WaitAsync(ct);

        try
        {
            if (_publisherChannel is {IsOpen: true})
                return _publisherChannel;

            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    HostName = options.Host,
                    Port = options.Port,
                    UserName = options.UserName,
                    Password = options.Password
                };
                _connection = await factory.CreateConnectionAsync(ct);
            }
            _publisherChannel = await _connection.CreateChannelAsync(cancellationToken: ct);

            await _publisherChannel.QueueDeclareAsync(
                queue: options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);
            
            return _publisherChannel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IChannel> GetConsumerChannelAsync(CancellationToken ct)
    {
        if (_consumerChannel is {IsOpen: true})
            return _consumerChannel;
        
        await _initLock.WaitAsync(ct);

        try
        {
            if (_consumerChannel is {IsOpen: true})
                return _consumerChannel;

            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    HostName = options.Host,
                    Port = options.Port,
                    UserName = options.UserName,
                    Password = options.Password
                };
                _connection = await factory.CreateConnectionAsync(ct);
            }
            _consumerChannel = await _connection.CreateChannelAsync(cancellationToken: ct);
            
            return _consumerChannel;
        }
        finally
        {
            _initLock.Release();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_publisherChannel is not null) await _publisherChannel.DisposeAsync();        
        if (_consumerChannel is not null) await _consumerChannel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();    
    }
}