using System.Diagnostics;

namespace Ingestor.Infrastructure.Telemetry;

public static class IngestorDatabaseActivitySource
{
    public const string Name = "Ingestor.Database";

    public static readonly ActivitySource Database = new(Name, "1.0.0");
}
