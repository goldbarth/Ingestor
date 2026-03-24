using RabbitMQ.Client;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqConnectionManager(RabbitMqOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    
    public async Task<IChannel> GetPublishChannelAsync(CancellationToken ct)
    {
        if (_channel is {IsOpen: true})
            return _channel;
        
        await _initLock.WaitAsync(ct);

        try
        {
            if (_channel is {IsOpen: true})
                return _channel;
            
            var factory = new ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.UserName,
                Password = options.Password
            };
            _connection = await factory.CreateConnectionAsync(ct);

            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            await _channel.QueueDeclareAsync(
                queue: options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);
            
            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();                                                          
        if (_connection is not null) await _connection.DisposeAsync();    
    }
}