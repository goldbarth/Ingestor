# ADR-005: Error Classification — Transient vs. Permanent

**Date:** 2026-03-18  
**Status:** Accepted

---

## Context

The background worker catches exceptions that escape the `ImportPipelineHandler` during job processing. Before this decision, every caught exception was treated identically: the job was scheduled for retry up to `MaxAttempts` times, regardless of whether retrying could ever succeed.

Two fundamentally different failure modes exist:

1. **Transient failures** — caused by temporary infrastructure problems (database connection timeouts, network blips, transient PostgreSQL errors). The underlying cause is expected to resolve on its own. Retrying after a delay is likely to succeed.

2. **Permanent failures** — caused by the job's data or an unrecoverable application state (malformed payload, unexpected `NullReferenceException` in processing logic, schema violations). Retrying the exact same data will produce the exact same failure. Retries waste resources and delay dead-lettering.

Without classification, a job with a corrupt payload would exhaust all three retry attempts — each one failing immediately — before being dead-lettered. This increases dead-letter latency by the sum of all retry delays (1s + 4s + 16s = 21s) with no possibility of recovery.

---

## Decision

We introduce an **`IExceptionClassifier`** interface in the `Application` layer and a concrete **`ExceptionClassifier`** implementation in the `Infrastructure` layer.

```csharp
// Application.Abstractions
public interface IExceptionClassifier
{
    ErrorCategory Classify(Exception exception);
}

// Infrastructure
public sealed class ExceptionClassifier : IExceptionClassifier
{
    public ErrorCategory Classify(Exception exception) => exception switch
    {
        NpgsqlException { IsTransient: true }                                    => ErrorCategory.Transient,
        DbUpdateException { InnerException: NpgsqlException { IsTransient: true } } => ErrorCategory.Transient,
        TimeoutException                                                          => ErrorCategory.Transient,
        _                                                                         => ErrorCategory.Permanent
    };
}
```

The worker uses the classification result to determine the next action:

- **Transient + retries remaining** → schedule retry with exponential backoff
- **Transient + retries exhausted** → dead-letter
- **Permanent** → dead-letter immediately, regardless of remaining retries

The interface lives in `Application` because the worker depends on `Application`, not on `Infrastructure`. The implementation lives in `Infrastructure` because `NpgsqlException` and `DbUpdateException` are infrastructure-layer types. This follows the same dependency-inversion pattern as `IImportJobRepository` / `ImportJobRepository`.

---

## Consequences

### Benefits

- **No wasted retries on permanent failures** — a job with a corrupt payload is dead-lettered on the first attempt instead of after three.
- **Correct audit trail** — `ImportAttempt` records carry the accurate `ErrorCategory`, enabling operators to distinguish transient spikes from recurring data problems.
- **Extensible by design** — the switch expression in `ExceptionClassifier` makes adding new transient exception types a one-line change. No branching logic to update elsewhere.
- **Dependency rule preserved** — `Application` and `Worker` are not coupled to Npgsql. The `IExceptionClassifier` abstraction keeps infrastructure details behind the interface boundary.

### Trade-offs

- **Unknown exceptions default to Permanent** — any exception type not explicitly listed is classified as Permanent. This is the safer default (avoids infinite retry loops) but could cause premature dead-lettering if a new transient infrastructure error is not yet recognized.
- **`NpgsqlException.IsTransient` is a third-party contract** — the classification relies on Npgsql correctly setting `IsTransient = true` for recoverable errors. If Npgsql's behaviour changes across versions, classification may silently degrade.
- **No circuit breaker** — classification acts per-exception, not per error rate. A sustained infrastructure outage will still exhaust all retries per job rather than pausing the entire worker.

### When to revisit

- Add new transient exception types to `ExceptionClassifier` whenever a new infrastructure dependency is introduced (e.g. an HTTP client, a blob storage SDK).
- Introduce a circuit breaker (e.g. Polly) if the infrastructure outage pattern causes mass dead-lettering across many jobs simultaneously.
- Reconsider the `_ => Permanent` default if operational data shows frequent false-positive permanent classifications.