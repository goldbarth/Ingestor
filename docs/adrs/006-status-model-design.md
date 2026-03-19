# ADR-006: Status Model Design — Explicit States and Strict Transitions

**Date:** 2026-03-19
**Status:** Accepted

---

## Context

The Ingestor system processes import jobs asynchronously across multiple stages: file intake, parsing, validation, and persistence. Each stage can fail independently, and failures must behave differently depending on their cause — a data problem should not be retried, an infrastructure problem should.

Two approaches were considered for tracking job state:

1. **Status flags** — a set of boolean or nullable-timestamp columns (e.g. `ParsedAt`, `ValidatedAt`, `FailedAt`) that record progress without enforcing any order
2. **Explicit state machine** — a single `Status` enum with a statically defined set of allowed transitions, enforced at the domain layer

Additionally, the granularity of states required a decision: a coarse model (e.g. `Pending`, `Running`, `Done`, `Failed`) is simpler but loses information about *where* in the pipeline a job failed.

---

## Decision

We use an **explicit state machine** with eight named states and a static allowed-transitions set enforced by the domain layer.

### The eight states

```
Received → Parsing → Validating → Processing → Succeeded
                │          │             │
                ├──→ ValidationFailed    └──→ ProcessingFailed ──→ DeadLettered
                └──→ ProcessingFailed
                                  (retry) ──→ Parsing
                          DeadLettered ──→ Received  (manual requeue)
                      ValidationFailed ──→ Received  (manual requeue)
```

| State             | Meaning                                                                 | Terminal? |
|-------------------|-------------------------------------------------------------------------|-----------|
| `Received`        | File accepted by API, outbox entry created, awaiting worker pickup      | No        |
| `Parsing`         | Worker has claimed the job and is parsing the raw payload               | No        |
| `Validating`      | Parsing succeeded, domain rules are being applied to parsed lines       | No        |
| `Processing`      | Validation passed, `DeliveryItem` records are being written to the DB   | No        |
| `Succeeded`       | All items persisted, job complete                                        | Yes       |
| `ValidationFailed`| Parsing or validation found data errors; no retry (requeue allowed)     | Yes*      |
| `ProcessingFailed`| A transient infrastructure error occurred; retry is scheduled           | No        |
| `DeadLettered`    | Retry attempts exhausted; manual intervention required (requeue allowed)| Yes*      |

*Requeue-able via `POST /api/imports/{id}/requeue`.

### Allowed transitions

Defined as a `HashSet<(JobStatus From, JobStatus To)>` in `ImportJobWorkflow`:

```csharp
(Received,         Parsing),
(Parsing,          Validating),
(Parsing,          ValidationFailed),
(Parsing,          ProcessingFailed),
(Validating,       Processing),
(Validating,       ValidationFailed),
(Processing,       Succeeded),
(Processing,       ProcessingFailed),
(ProcessingFailed, Parsing),        // Retry: worker re-enters at Parsing
(ProcessingFailed, DeadLettered),   // Retries exhausted
(DeadLettered,     Received),       // Manual requeue
(ValidationFailed, Received),       // Manual requeue after operator correction
```

Any transition not in this set throws `DomainException("job.invalid_transition")`.

---

## Consequences

### Benefits

- **Illegal states are unrepresentable** — the domain enforces that a job can never jump from `Received` to `Succeeded`, or from `Succeeded` back to `Processing`. Bugs that corrupt job state are caught at the point of occurrence rather than discovered later through inconsistent data.

- **Failure cause is preserved in the state** — `ValidationFailed` and `ProcessingFailed` are distinct states with distinct semantics. An operator querying failed jobs can immediately distinguish data problems (which require a corrected file) from infrastructure problems (which may self-resolve). A coarse `Failed` state would lose this information.

- **Retry loop is expressed in the state model** — `ProcessingFailed` is an intermediate state, not a terminal one. The transition `ProcessingFailed → Parsing` makes the retry mechanism explicit: the worker re-enters the pipeline at the parsing step, re-reading the persisted `ImportPayload`. Returning to `Received` would be semantically incorrect — the file is not being received again, it is being re-processed from storage.

- **Terminal states are unambiguous** — `CompletedAt` is set only when transitioning to `Succeeded`, `ValidationFailed`, or `DeadLettered`. `ProcessingFailed` deliberately does not set `CompletedAt` because it is not terminal. This means duration calculations are always accurate.

- **Full history is reconstructible** — because every state change produces an `AuditEvent`, the complete processing history of a job (including retries) can be replayed from the audit log. This would not be possible with a flag-based model.

### Trade-offs

- **More states than a naïve model** — eight states require more test coverage and more cases in any code that switches on `JobStatus`. A two-state model (`Pending` / `Done`) would require no transition enforcement but would sacrifice all diagnostic value.

- **Requeue of `ValidationFailed` replays the same payload** — the `ValidationFailed → Received` transition is intended for cases where the validation rules themselves were incorrect or have been updated. If the original file had genuine data errors, requeueing it will produce the same `ValidationFailed` outcome. There is currently no mechanism to substitute a corrected payload during requeue.

- **Transitions are centralised, not per-state** — the allowed-transitions set in `ImportJobWorkflow` is a single list of all valid pairs. This is simple and easy to scan, but means adding a new state requires editing one central location rather than the state class itself. For the current number of states this is not a problem.

### When to revisit

- Add new states if new pipeline steps are introduced (e.g. an enrichment step between validation and processing).
- Consider per-state allowed-next-state lists if the transition table grows beyond ~20 entries.
- If `ValidationFailed → Received` requeue with payload replacement is needed, extend the requeue endpoint to accept an optional new file upload.