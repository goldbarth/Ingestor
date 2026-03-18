# ADR-007: OpenTelemetry Tracing for Pipeline Observability

**Date:** 2026-03-18
**Status:** Accepted

---

## Context

The `ImportPipelineHandler` orchestrates three sequential processing steps — Parsing, Validating, and Processing — before a job reaches a terminal state. Without tracing, diagnosing latency problems or failures requires correlating log lines across multiple log entries, which is error-prone especially under concurrent load.

Two observability approaches were considered:

1. **Structured logging only** — log a message with timestamps at the start and end of each step. Duration is derivable by diffing timestamps.
2. **Distributed tracing (OpenTelemetry)** — instrument each step as a span with start time, end time, status, and tags. Spans form a tree that represents the full execution of a job.

---

## Decision

We use **OpenTelemetry tracing via `System.Diagnostics.ActivitySource`** with a Console Exporter for V1.

A static `ActivitySource` is defined in the `Application` layer:

```csharp
public static class IngestorActivitySource
{
    public const string Name = "Ingestor.Pipeline";
    public static readonly ActivitySource Pipeline = new(Name, "1.0.0");
}
```

Each pipeline step creates a child span with the job ID as a tag:

```csharp
using var activity = IngestorActivitySource.Pipeline.StartActivity("pipeline.parsing");
activity?.SetTag("job.id", jobId.Value.ToString());
```

On failure, the span status is set explicitly:

```csharp
activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
```

OTel registration and exporter configuration live in the host projects (API, Worker) via `AddOpenTelemetry().WithTracing(...)`.

### Why `ActivitySource` in the Application layer?

`System.Diagnostics.ActivitySource` is part of the .NET BCL — no NuGet package is needed to create spans. The `Application` layer can instrument its own logic without taking a dependency on any OTel package, preserving the dependency rules. Only the host projects (`Api`, `Worker`) reference the OTel exporter packages.

### Why Console Exporter for V1?

The Console Exporter requires no additional infrastructure (no collector, no sidecar) and writes structured span data directly to stdout, which is captured by Docker and any log aggregation stack. It is sufficient to verify that spans are emitted correctly and that the `job.id` tag is present.

OTLP export (for Jaeger, Grafana Tempo, or any OpenTelemetry Collector) is the intended production path and requires only swapping `AddConsoleExporter()` for `AddOtlpExporter()` with a collector endpoint — no application code changes.

### The null-Activity pattern

`ActivitySource.StartActivity()` returns `null` when no listener is registered (e.g. in unit tests, or when OTel is not configured). All span interactions use the null-conditional operator (`?.`) to safely no-op in that case. This means instrumentation has zero overhead when tracing is disabled.

---

## Consequences

### Benefits

- **Per-step visibility** — each of the three pipeline steps is a discrete span with its own start time, end time, duration, and status. Slow steps are immediately identifiable without log diffing.
- **Job correlation** — the `job.id` tag is present on every span, enabling filtering of all traces for a specific job in any trace backend.
- **Zero overhead when inactive** — the null-Activity pattern ensures no allocations or timing overhead in unit tests or environments without OTel configured.
- **No Application-layer OTel dependency** — `System.Diagnostics.ActivitySource` is BCL. The dependency rule (Application → Domain only for package deps) is preserved.
- **Backend-agnostic** — swapping the exporter (Console → OTLP → Jaeger) requires no changes to the instrumented code.

### Trade-offs

- **Unhandled exceptions are not auto-tagged** — if an unexpected exception propagates out of a `using` block, the span is disposed (ended) but its status remains `Ok`. A `try/catch` with `SetStatus(Error)` and rethrow inside each span would be needed to mark unhandled exceptions as errors. This is not implemented in V1 because all expected failure paths are handled explicitly via `PipelineResult`.
- **`RecordException` not available in Application** — `activity.RecordException(ex)` (which attaches exception details as a span event) is defined in the `OpenTelemetry` package, which `Application` must not reference. Exception details are therefore only available via logs, not traces.
- **Console Exporter is not production-grade** — it writes human-readable text, not OTLP. A production deployment requires an OTLP-capable exporter and a collector.

### When to revisit

- Add `AddOtlpExporter()` and configure a collector in Docker Compose when a trace backend (Jaeger, Grafana Tempo) is introduced.
- Add `try/catch` + `SetStatus(Error)` inside span blocks if unhandled exceptions in the pipeline become a recurring operational issue.
- Add a root span for the entire pipeline (parent of the three step spans) to represent the full job processing duration as a single trace.