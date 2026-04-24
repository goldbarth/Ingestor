using System.Diagnostics;

namespace Ingestor.Infrastructure;

public static class IngestorMessagingActivitySource
{
    public const string Name = "Ingestor.Messaging";

    public static readonly ActivitySource Messaging = new(Name, "1.0.0");
}
