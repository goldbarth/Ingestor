# ADR-012: Timeout-Based Recovery for Stale Outbox Locks

**Date:** 2026-03-20
**Status:** Accepted

---

## Context

ADR-003 documents a known gap in the pessimistic locking strategy: if a worker crashes, is killed, or loses its database connection after claiming an `OutboxEntry` (status set to `Processing`) but before completing it (status set to `Done`), the entry remains stuck in `Processing` indefinitely. No other worker will claim it, because `ClaimNextAsync` selects only `Pending` entries.

This produces a silent failure mode: the affected job stays in its last persisted status (typically `Received`) with no further processing, no error recorded, and no alert — until an operator manually investigates.

Two recovery strategies are commonly considered:

1. **Heartbeat / lease renewal** — the worker continuously refreshes a `LockedUntil` timestamp while processing. Entries whose `LockedUntil` has expired are treated as abandoned. Requires a background renewal loop per active entry and increases write pressure.

2. **Timeout-based recovery** — any entry that has been in `Processing` for longer than a configured threshold is assumed to be orphaned and reset to `Pending`. Simpler to implement; the threshold is a proxy for "no healthy worker could take this long".

---

## Decision

We use **timeout-based recovery** via a `RecoverStaleAsync(TimeSpan timeout)` method called at the start of every worker poll cycle.

### Recovery query

```sql
UPDATE outbox_entries
SET "Status" = 'Pending', "LockedAt" = NULL
WHERE "Id" IN (
    SELECT "Id" FROM outbox_entries
    WHERE "Status" = 'Processing'
      AND "LockedAt" < NOW() - {timeout}
    FOR UPDATE SKIP LOCKED
)
```

`FOR UPDATE SKIP LOCKED` on the subquery ensures that if two workers attempt recovery simultaneously, each can only reset a distinct set of entries. No entry will be reset by both workers concurrently.

### Implementation details

- Recovery runs **before** `ClaimNextAsync` in each poll cycle, using the same DI scope.
- The method returns the count of recovered entries; the worker logs a message only when `count > 0` to keep logs clean in the normal case.
- The timeout is configurable via `WorkerOptions.StaleLockTimeoutSeconds`, defaulting to **300 seconds (5 minutes)**.
- Recovery is implemented as a raw SQL bulk update (`ExecuteSqlAsync`) — not via EF change tracking — because loading and modifying individual entities for a batch operation is unnecessary overhead, and this is an infrastructure-level correction with no domain business meaning.

### Default timeout: 300 seconds

The default of 5 minutes is a deliberate trade-off:

- **Too short** (e.g. 30 s): a legitimately slow pipeline run (large file, slow DB) could be incorrectly classified as stale. The entry would be reset to `Pending` while the original worker is still processing, enabling concurrent duplicate processing of the same job.
- **Too long** (e.g. 1 h): a genuinely crashed worker leaves jobs blocked for an unacceptably long time.
- **5 minutes** sits well above the maximum legitimate processing time observed in tests and comfortably above the maximum retry backoff (`4^3 = 64 s`), while keeping recovery lag within operational tolerance.

---

## Consequences

### Benefits

- **Eliminates the permanent stuck state** — orphaned entries are automatically recovered within one polling cycle after the timeout expires. No manual operator intervention is required.
- **Concurrency-safe** — `FOR UPDATE SKIP LOCKED` guarantees that parallel recovery attempts do not race on the same entry.
- **Low complexity** — no heartbeat infrastructure, no additional columns. The existing `LockedAt` timestamp is sufficient.
- **Configurable** — operators can tune `StaleLockTimeoutSeconds` to match actual processing time characteristics of their deployment.

### Trade-offs

- **At-least-once delivery** — recovery intentionally allows a job to be processed more than once. If the original worker completes after recovery has reset the entry, a second worker may claim and re-process it. This is mitigated by the idempotency key on `ImportJob` (see ADR-002) and by the pipeline's transactional status transitions, but callers must be aware that `DeliveryItem` records could be written twice for the same job in this edge case.
- **Timeout is a heuristic** — the threshold is a best-effort estimate. A severely degraded-but-alive worker (e.g. a long GC pause, a blocked thread) could still be processing when its entry is recovered. In V1, this risk is accepted given the single-worker deployment model.
- **No per-entry timeout granularity** — all entries share the same timeout regardless of payload size or expected processing complexity. A single large-file import and a small-file import are treated identically.

### Why not heartbeat / lease renewal?

A heartbeat requires the worker to maintain a background renewal loop for each in-flight entry. This adds implementation complexity (concurrent timer management, failure handling for the renewal itself) and increases write load on the `outbox_entries` table. Given V1's single-worker deployment model and the low frequency of genuine crashes, the simpler timeout approach provides sufficient safety at significantly lower cost.

### When to revisit

- If processing times become highly variable (e.g. files exceeding several hundred MB), consider per-entry configurable timeouts or a separate `ProcessingDeadline` column.
- If horizontal scaling introduces multiple concurrent workers with diverse processing speeds, the heartbeat model becomes more attractive to avoid false-positive recovery of slow-but-alive workers.
- If at-most-once delivery semantics become a hard requirement, a compensation step (checking whether `DeliveryItems` were already written before re-inserting) would be needed in the pipeline.