# Runbook — Ingestor

> This document describes operational procedures for investigating and resolving common issues in the Ingestor system.

---

## Quick Reference

| Action                 | Endpoint / Command                          | Notes                                              |
|------------------------|---------------------------------------------|----------------------------------------------------|
| Check system health    | `GET /health`                               | Returns `Healthy` / `Unhealthy` + component detail |
| List failed jobs       | `GET /api/imports?status=DeadLettered`      | Add `&cursor=<id>&pageSize=25` to paginate         |
| List validation errors | `GET /api/imports?status=ValidationFailed`  | Data problems, no automatic retry                  |
| Inspect job detail     | `GET /api/imports/{id}`                     | Includes `currentAttempt`, `lastErrorCode`         |
| View job audit history | `GET /api/imports/{id}/history`             | Chronological list of all status transitions       |
| Requeue a failed job   | `POST /api/imports/{id}/requeue`            | Only for `DeadLettered` or `ValidationFailed`      |
| Check worker heartbeat | `GET /health` on Worker Host                | Reports `Unhealthy` if heartbeat is stale          |

---

## Investigating Dead-Lettered Jobs

### Symptoms

- Job status is `DeadLettered`
- All retry attempts exhausted (`currentAttempt` equals `maxAttempts`)

### Steps

1. **Find the job**

   ```
   GET /api/imports?status=DeadLettered
   ```

   Note the job `id`.

2. **Inspect job detail**

   ```
   GET /api/imports/{id}
   ```

   Check:
   - `lastErrorCode` — machine-readable error code
   - `lastErrorMessage` — human-readable description
   - `currentAttempt` / `maxAttempts` — confirms retries were exhausted

3. **Read the full audit history**

   ```
   GET /api/imports/{id}/history
   ```

   The history shows every status transition in chronological order, including each `ProcessingFailed → Parsing` retry cycle and the final `ProcessingFailed → DeadLettered` transition.

4. **Determine root cause**

   Use `lastErrorCode` and the audit history to identify the failure pattern:

   - Repeated `ProcessingFailed` with the same error code → likely a permanent infrastructure problem (e.g. DB schema mismatch, missing config)
   - First attempt `ValidationFailed`, then moved to dead-letter manually → data problem
   - Intermittent `ProcessingFailed` with different error codes → transient infrastructure instability

5. **Decide: requeue or discard**

   - **Requeue** if the root cause has been resolved (infrastructure was fixed, config was corrected) → see [Manual Requeue](#manual-requeue)
   - **Discard** if the file itself has unrecoverable data errors and cannot be corrected by the supplier

### Common Causes

| Cause                            | Error Category | Resolution                                                   |
|----------------------------------|----------------|--------------------------------------------------------------|
| DB connection timeout            | Transient      | Check DB host availability; requeue once stable              |
| Malformed CSV / JSON             | Permanent      | Ask supplier to resend corrected file; do not requeue        |
| Missing required fields          | Permanent      | Ask supplier to resend corrected file; do not requeue        |
| Invalid date format              | Permanent      | Ask supplier to use ISO 8601 format (`YYYY-MM-DD`)           |
| Quantity ≤ 0                     | Permanent      | Ask supplier to correct line items; do not requeue           |
| Duplicate idempotency key        | —              | No action needed; original job is still active               |
| Worker crashed mid-processing    | Transient      | Stale lock recovered automatically; job will retry           |

---

## Investigating Stuck Jobs

### Symptoms

- Job has been in `Parsing`, `Validating`, or `Processing` for longer than expected
- New jobs remain in `Received` without transitioning
- Worker heartbeat is stale on `GET /health`

### Steps

1. **Check the worker health endpoint**

   ```
   GET /health   (Worker Host)
   ```

   A stale heartbeat means the worker process has stopped or is unresponsive. Restart the worker container.

2. **Check if the worker is running**

   ```bash
   docker compose ps
   docker compose logs worker --tail=50
   ```

   Look for crash messages, unhandled exceptions, or DB connectivity errors.

3. **Identify jobs stuck in a processing state**

   ```
   GET /api/imports?status=Processing
   GET /api/imports?status=Parsing
   GET /api/imports?status=Validating
   ```

   If a job has been in one of these states beyond the expected processing window, the outbox entry for that job may have a stale lock. The worker's stale-lock recovery mechanism will reclaim it automatically after the configured timeout. If recovery does not occur, restart the worker.

4. **Check outbox entries directly (if DB access is available)**

   ```sql
   SELECT * FROM outbox_entries WHERE status = 'Processing' ORDER BY locked_at;
   ```

   Entries with a `locked_at` timestamp older than the stale-lock timeout are candidates for recovery.

---

## Manual Requeue

### When to Requeue

- Job is `DeadLettered` and the underlying infrastructure problem has been resolved
- Job is `DeadLettered` due to a transient error that is now unlikely to recur
- Job is `ValidationFailed` because a validation rule was incorrect or overly strict and has since been corrected

### When NOT to Requeue

- Job is `Succeeded` — requeue returns `409 Conflict`
- Job is currently in an active processing state (`Parsing`, `Validating`, `Processing`)
- The file itself has permanent data errors that have not been corrected — requeueing will produce the same `ValidationFailed` outcome

### Procedure

1. Confirm the job is in a requeue-able state (`DeadLettered` or `ValidationFailed`):

   ```
   GET /api/imports/{id}
   ```

2. Confirm the root cause has been resolved.

3. Submit the requeue request:

   ```
   POST /api/imports/{id}/requeue
   ```

   Expected response: `202 Accepted` with `Location: /api/imports/{id}`

4. Monitor the job status:

   ```
   GET /api/imports/{id}
   ```

   The job should transition: `Received → Parsing → Validating → Processing → Succeeded`

5. Verify by checking the audit history:

   ```
   GET /api/imports/{id}/history
   ```

   The history will show the requeue event with `triggeredBy: API` followed by the new processing cycle.

---

## Worker Not Processing Jobs

### Symptoms

- New jobs stay in `Received` indefinitely
- Outbox entries remain `Pending`

### Steps

1. **Check worker health**

   ```
   GET /health   (Worker Host)
   ```

2. **Check worker logs**

   ```bash
   docker compose logs worker --tail=100
   ```

   Look for:
   - DB connection errors (worker cannot reach PostgreSQL)
   - Unhandled exceptions in the processing loop
   - Application startup failures

3. **Verify PostgreSQL is reachable**

   ```bash
   docker compose ps db
   docker compose logs db --tail=20
   ```

4. **Restart the worker if it has crashed**

   ```bash
   docker compose restart worker
   ```

5. **Verify polling resumes**

   After restart, new `Received` jobs should transition to `Parsing` within one polling interval (default: 5 seconds).

---

## Health Check Failures

### API Host

`GET /health` returns `Unhealthy`:

- **DB check failed** — the API cannot reach PostgreSQL. Check DB container status and connection string configuration in `appsettings.json` / environment variables.
- **Partial degradation** — some checks healthy, others not. Inspect the response body for per-component status.

### Worker Host

`GET /health` returns `Unhealthy`:

- **DB check failed** — same as API; check PostgreSQL connectivity.
- **Heartbeat stale** — the worker's background loop has not updated its heartbeat within the expected window. The worker process may be deadlocked or overwhelmed. Check worker logs and restart if necessary.

---

## Log Investigation

### Finding Logs for a Specific Job

All log entries produced while processing a job are enriched with the `JobId` property. To find all logs for a specific job:

```bash
# Docker Compose — filter by JobId
docker compose logs worker | grep "<job-id>"
docker compose logs api    | grep "<job-id>"
```

In a structured log aggregator (e.g. Seq, Grafana Loki), filter by:

```
JobId = "<job-id>"
```

### Common Log Patterns

| Log message pattern                                      | Meaning                                               |
|----------------------------------------------------------|-------------------------------------------------------|
| `Claimed outbox entry {EntryId} for job {JobId}`         | Worker picked up a job for processing                 |
| `Pipeline succeeded for job {JobId}`                     | All steps completed, DeliveryItems persisted          |
| `Pipeline failed for job {JobId}: {ErrorCode}`           | Permanent failure — job will not be retried           |
| `Transient failure for job {JobId}, attempt {N}`         | Retry scheduled with exponential backoff              |
| `Job {JobId} dead-lettered after {N} attempts`           | All retries exhausted, DeadLetterEntry created        |
| `Job {JobId} requeued`                                   | Manual requeue accepted, new OutboxEntry created      |

---

## Appendix

### Status Model Reference

```
Received → Parsing → Validating → Processing → Succeeded
               │           │            │
               │           └──→ ValidationFailed  (terminal, requeue allowed)
               │
               └──→ ProcessingFailed ──→ (retry) ──→ Parsing
               └──→ ValidationFailed             └──→ DeadLettered  (terminal, requeue allowed)

DeadLettered     → Received  (manual requeue)
ValidationFailed → Received  (manual requeue)
```

### Error Categories

| Category    | Retryable | Examples                                      |
|-------------|-----------|-----------------------------------------------|
| `Transient` | Yes       | DB connection timeout, network error          |
| `Permanent` | No        | Malformed CSV, missing fields, invalid values |

### HTTP Status Codes

| Status | Meaning                                              |
|--------|------------------------------------------------------|
| 201    | Job created successfully                             |
| 200    | Duplicate upload — existing job returned             |
| 202    | Requeue accepted                                     |
| 400    | Invalid request (unsupported content type, bad input) |
| 404    | Job not found                                        |
| 409    | Conflict (requeue not allowed in current state)      |
| 500    | Unexpected server error                              |
