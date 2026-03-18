using System.Diagnostics;

namespace Ingestor.Application.Telemetry;

public static class IngestorActivitySource
{
    public const string Name = "Ingestor.Pipeline";

    public static readonly ActivitySource Pipeline = new(Name, "1.0.0");
}