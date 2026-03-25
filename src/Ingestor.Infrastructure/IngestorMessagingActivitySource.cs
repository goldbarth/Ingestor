using System.Diagnostics;

namespace Ingestor.Infrastructure.Telemetry;

public static class IngestorMessagingActivitySource
{
    public const string Name = "Ingestor.Messaging";

    public static readonly ActivitySource Messaging = new(Name, "1.0.0");
}
