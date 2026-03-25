namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string QueueName { get; init; } = "import-jobs";

    public string DeadLetterExchangeName => $"{QueueName}.dlx";
    public string DeadLetterQueueName    => $"{QueueName}.dead-letters";
}