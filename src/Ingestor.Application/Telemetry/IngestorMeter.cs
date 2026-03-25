using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ingestor.Application.Telemetry;

public static class IngestorMeter
{
    public const string Name = "Ingestor.Application";

    private static readonly Meter Meter = new(Name, "1.0.0");

    private static readonly Counter<long> PipelineRuns = Meter.CreateCounter<long>(
        "ingestor.pipeline.runs",
        unit: "{run}",
        description: "Completed import pipeline executions grouped by outcome.");

    private static readonly Counter<long> ProcessedItems = Meter.CreateCounter<long>(
        "ingestor.pipeline.items_processed",
        unit: "{item}",
        description: "Delivery items emitted by successful import pipeline executions.");

    private static readonly Histogram<double> PipelineDuration = Meter.CreateHistogram<double>(
        "ingestor.pipeline.duration",
        unit: "ms",
        description: "End-to-end duration of import pipeline executions.");

    private static readonly Histogram<double> PipelineStepDuration = Meter.CreateHistogram<double>(
        "ingestor.pipeline.step.duration",
        unit: "ms",
        description: "Duration of individual import pipeline steps.");

    public static void RecordPipelineRun(
        string outcome,
        double durationMs,
        int processedItemCount = 0,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "outcome", outcome }
        };

        if (!string.IsNullOrWhiteSpace(errorCode))
            tags.Add("error.code", errorCode);

        PipelineRuns.Add(1, tags);
        PipelineDuration.Record(durationMs, tags);

        if (processedItemCount > 0)
        {
            ProcessedItems.Add(processedItemCount, new TagList
            {
                { "outcome", outcome }
            });
        }
    }

    public static void RecordPipelineStepDuration(string step, string outcome, double durationMs)
    {
        PipelineStepDuration.Record(durationMs, new TagList
        {
            { "step", step },
            { "outcome", outcome }
        });
    }
}
