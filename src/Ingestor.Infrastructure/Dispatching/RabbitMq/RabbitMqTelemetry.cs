using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ingestor.Infrastructure.Dispatching.RabbitMq;

internal static class RabbitMqTelemetry
{
    private const string TraceParentHeaderName = "traceparent";
    private const string TraceStateHeaderName = "tracestate";

    public static BasicProperties CreateBasicProperties(Activity? activity, Guid jobId)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = jobId.ToString(),
            CorrelationId = activity?.TraceId.ToString()
        };

        if (activity is null)
            return properties;

        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [TraceParentHeaderName] = activity.Id
        };

        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
            headers[TraceStateHeaderName] = activity.TraceStateString;

        properties.Headers = headers;
        return properties;
    }

    public static Activity? StartConsumerActivity(
        ActivitySource activitySource,
        BasicDeliverEventArgs args,
        RabbitMqOptions options,
        Guid? jobId = null)
    {
        var parentContext = ExtractParentContext(args.BasicProperties);

        var activity = parentContext.HasValue
            ? activitySource.StartActivity(
                $"rabbitmq receive {options.QueueName}",
                ActivityKind.Consumer,
                parentContext.Value)
            : activitySource.StartActivity(
                $"rabbitmq receive {options.QueueName}",
                ActivityKind.Consumer);

        SetMessagingTags(
            activity,
            options,
            operationType: "process",
            operationName: "process",
            jobId,
            deliveryTag: args.DeliveryTag,
            messageId: args.BasicProperties?.MessageId,
            conversationId: args.BasicProperties?.CorrelationId,
            bodySize: args.Body.Length);
        return activity;
    }

    public static Activity? StartProducerActivity(
        ActivitySource activitySource,
        RabbitMqOptions options,
        Guid jobId,
        int bodySize)
    {
        var activity = activitySource.StartActivity(
            $"rabbitmq publish {options.QueueName}",
            ActivityKind.Producer);

        SetMessagingTags(
            activity,
            options,
            operationType: "send",
            operationName: "send",
            jobId,
            messageId: jobId.ToString(),
            conversationId: activity?.TraceId.ToString(),
            bodySize: bodySize);
        return activity;
    }

    private static ActivityContext? ExtractParentContext(IReadOnlyBasicProperties? basicProperties)
    {
        if (basicProperties?.Headers is null)
            return null;

        if (!TryReadHeader(basicProperties.Headers, TraceParentHeaderName, out var traceParent))
            return null;

        TryReadHeader(basicProperties.Headers, TraceStateHeaderName, out var traceState);

        return ActivityContext.TryParse(traceParent!, traceState, isRemote: true, out var parentContext)
            ? parentContext
            : null;
    }

    private static void SetMessagingTags(
        Activity? activity,
        RabbitMqOptions options,
        string operationType,
        string operationName,
        Guid? jobId,
        ulong? deliveryTag = null,
        string? messageId = null,
        string? conversationId = null,
        int? bodySize = null)
    {
        if (activity is null)
            return;

        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.destination.name", options.QueueName);
        activity.SetTag("messaging.operation.type", operationType);
        activity.SetTag("messaging.operation.name", operationName);
        activity.SetTag("messaging.rabbitmq.destination.routing_key", options.QueueName);
        activity.SetTag("server.address", options.Host);
        activity.SetTag("server.port", options.Port);
        activity.SetTag("network.peer.address", options.Host);
        activity.SetTag("network.peer.port", options.Port);
        activity.SetTag("peer.service", "rabbitmq");
        activity.SetTag("messaging.destination_kind", "queue");
        activity.SetTag("messaging.rabbitmq.routing_key", options.QueueName);
        activity.SetTag("messaging.operation", operationType);

        if (jobId.HasValue)
            activity.SetTag("job.id", jobId.Value);

        if (deliveryTag.HasValue)
            activity.SetTag("messaging.rabbitmq.message.delivery_tag", deliveryTag.Value);

        if (!string.IsNullOrWhiteSpace(messageId))
            activity.SetTag("messaging.message.id", messageId);

        if (!string.IsNullOrWhiteSpace(conversationId))
            activity.SetTag("messaging.message.conversation_id", conversationId);

        if (bodySize.HasValue)
            activity.SetTag("messaging.message.body.size", bodySize.Value);
    }

    private static bool TryReadHeader(
        IDictionary<string, object?> headers,
        string headerName,
        out string? value)
    {
        value = null;

        if (!headers.TryGetValue(headerName, out var rawValue) || rawValue is null)
            return false;

        value = rawValue switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            string text => text,
            _ => rawValue.ToString()
        };

        return !string.IsNullOrWhiteSpace(value);
    }
}
