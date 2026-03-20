# ADR-003: Pessimistic Locking with SKIP LOCKED for Outbox Processing

**Date:** 2026-03-18
**Status:** Accepted

---

## Context

The background worker polls the `outbox_entries` table to claim jobs for processing. In a horizontally scaled deployment, multiple worker instances run concurrently against the same database. Without coordination, two workers could claim the same outbox entry simultaneously, causing the same job to be processed twice.

Two standard approaches exist to prevent this:

1. **Optimistic locking** — read the entry freely, then attempt an update with a version/ETag check (`WHERE id = ? AND status = 'Pending'`). If zero rows are affected, another worker claimed it first; retry with the next entry.

2. **Pessimistic locking** — acquire a row-level exclusive lock at read time (`SELECT ... FOR UPDATE`). Other workers are blocked or skipped until the lock is released.

---

## Decision

We use **pessimistic locking with `FOR UPDATE SKIP LOCKED`**.

```sql
SELECT "Id", "JobId", "Status", "CreatedAt", "ScheduledFor", "LockedAt", "ProcessedAt"
FROM outbox_entries
WHERE "Status" = 'Pending'
  AND ("ScheduledFor" IS NULL OR "ScheduledFor" <= NOW())
ORDER BY "CreatedAt"
LIMIT 1
FOR UPDATE SKIP LOCKED
```

The claim protocol is:
1. `BEGIN` transaction
2. Execute the query above — atomically selects and locks the next available entry
3. Update `Status = 'Processing'` and set `LockedAt = NOW()`
4. `COMMIT` — releases the row lock; the status change persists and prevents re-claiming

`SKIP LOCKED` instructs PostgreSQL to skip any rows that are currently locked by another transaction, rather than waiting for them. Each worker immediately receives its own distinct entry with no blocking or retrying.

A composite index on `(Status, CreatedAt)` supports the `WHERE` filter and `ORDER BY` efficiently.

---

## Consequences

### Benefits

- **No duplicate processing** — the row-level lock makes the read-and-claim operation atomic. Two workers cannot claim the same entry; one will skip it.
- **No thundering herd** — with optimistic locking, N workers competing for the same entry would all read it, then N-1 would fail and retry. With `SKIP LOCKED`, each worker independently and immediately claims a different entry. There is no retry loop.
- **Short lock window** — the exclusive lock is held only for the duration of the claim transaction (typically < 5 ms). Long-running job processing happens outside the transaction, after the lock is released.
- **No schema overhead** — optimistic locking requires a version column or ETag. `SKIP LOCKED` requires no additional columns.
- **Handles delayed retries** — the `ScheduledFor` predicate natively integrates retry backoff scheduling. Entries not yet due are invisible to workers without any application-side filtering.

### Trade-offs

- **Strict FIFO ordering is not guaranteed** — `SKIP LOCKED` skips locked rows. If Worker A holds a lock on the oldest entry, Worker B will claim the second-oldest. Worker B may complete before Worker A, breaking arrival-time ordering across concurrent workers. For independent import jobs, this is acceptable.
- **PostgreSQL-specific syntax** — `FOR UPDATE SKIP LOCKED` is a PostgreSQL extension (also available in Oracle and MySQL 8.0+, but not in all RDBMS). Migrating to a different database would require this query to be rewritten.
- **`Processing` rows require a recovery mechanism** — if a worker crashes after claiming an entry (status set to `Processing`) but before completing it (status set to `Done`), the entry remains stuck in `Processing` indefinitely. This gap is addressed by the timeout-based stale-lock recovery described in ADR-012.

### Why not optimistic locking?

Optimistic locking is well-suited to scenarios where contention is rare and reads vastly outnumber writes. An outbox queue is the opposite: every read is also a write (the claim), and contention is the normal case when multiple workers are active. The retry loop in optimistic locking adds code complexity and database round-trips with no benefit over `SKIP LOCKED`, which solves the same problem atomically and unconditionally.

### When to revisit

- If strict per-supplier FIFO ordering becomes a requirement, a single-worker-per-supplier model (partitioned claiming) would be needed, which changes the locking strategy significantly.
- If a `Processing`-stuck recovery mechanism is added in a future milestone, consider adding a `LockedUntil` timestamp column to bound the lock window and enable automatic re-queuing of abandoned entries.
