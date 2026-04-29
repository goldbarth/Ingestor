# Runbook — Ingestor

> This document describes operational procedures for investigating and resolving common issues in the Ingestor system.

---

## Quick Reference

| Action                        | Endpoint / Command                             | Notes                                              |
|-------------------------------|------------------------------------------------|----------------------------------------------------|
| Check system health           | `GET /health`                                  | Returns `Healthy` / `Unhealthy` + component detail |
| List failed jobs              | `GET /api/imports?status=DeadLettered`         | Add `&cursor=<id>&pageSize=25` to paginate         |
| List validation errors        | `GET /api/imports?status=ValidationFailed`     | Data problems, no automatic retry                  |
| List partial successes        | `GET /api/imports?status=PartiallySucceeded`   | Batch jobs where some chunks failed                |
| Inspect job detail            | `GET /api/imports/{id}`                        | Includes `currentAttempt`, `lastErrorCode`, batch progress |
| View job audit history        | `GET /api/imports/{id}/history`                | Chronological list of all status transitions       |
| Requeue a failed job          | `POST /api/imports/{id}/requeue`               | For `DeadLettered`, `ValidationFailed`, or `PartiallySucceeded` |
| Check worker heartbeat        | `GET /health` on Worker Host                   | Reports `Unhealthy` if heartbeat is stale          |
| RabbitMQ Management UI        | `http://localhost:15672`                       | Queue depth, connections, dead-letter queue        |

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

| Log message pattern                                                       | Meaning                                               |
|---------------------------------------------------------------------------|-------------------------------------------------------|
| `Claimed outbox entry {EntryId} for job {JobId}`                          | Worker picked up a job (DB strategy)                  |
| `Pipeline succeeded for job {JobId}`                                      | All steps completed, DeliveryItems persisted          |
| `Pipeline partially succeeded for job {JobId}`                            | Batch job completed with chunk failures               |
| `Chunk {N}/{Total} failed for job {JobId}. Lines in chunk: {Count}.`      | One chunk failed (Warning); pipeline continues        |
| `Pipeline failed for job {JobId}: {ErrorCode}`                            | Permanent failure — job will not be retried           |
| `Transient failure for job {JobId}, attempt {N}`                          | Retry scheduled with exponential backoff              |
| `Job {JobId} dead-lettered after {N} attempts`                            | All retries exhausted, DeadLetterEntry created        |
| `Job {JobId} requeued`                                                    | Manual requeue accepted, new OutboxEntry created      |
| `RabbitMQ connection failed … retrying in {N}s`                           | Broker unreachable at startup; will retry             |

---

---

## RabbitMQ Connection Issues

> Applies when `Dispatch:Strategy = RabbitMQ`.

### Symptoms

- New jobs stay in `Received` indefinitely
- Worker logs show `RabbitMQ connection failed` or `BrokerUnreachableException`
- API logs show publish errors immediately after job creation

### Steps

1. **Check that RabbitMQ is running**

   ```bash
   docker compose ps rabbitmq
   docker compose logs rabbitmq --tail=30
   ```

2. **Verify broker connectivity from the worker container**

   ```bash
   docker compose exec worker curl -f http://rabbitmq:15672/api/healthchecks/node \
     -u guest:<RABBITMQ_PASSWORD>
   ```

   Expected response: `{"status":"ok"}`.

3. **Check the Management UI**

   Open `http://localhost:15672` (default credentials: `guest` / `<RABBITMQ_PASSWORD>`).

   Verify:
   - The `import-jobs` queue exists and is not in an error state
   - Connections from both the API publisher and Worker consumer are listed under **Connections**

4. **Inspect reconnect behaviour**

   The `RabbitMqConnectionManager` retries the initial connection with a configurable interval
   (default: 5 seconds). If the broker was temporarily unavailable at startup, restart the
   affected container once the broker is healthy:

   ```bash
   docker compose restart api worker
   ```

5. **Fall back to the database strategy temporarily**

   If the broker is unavailable and jobs must continue processing:

   ```bash
   docker compose stop api worker
   # Edit docker-compose.yml: set Dispatch__Strategy=Database on both services
   docker compose up -d api worker
   ```

   See [Switching dispatch strategies](#switching-dispatch-strategies).

---

## Queue Inspection via Management UI

Open `http://localhost:15672` and log in with the configured RabbitMQ credentials.

### Queues

Navigate to **Queues and Streams**. Key queues:

| Queue | Purpose |
|---|---|
| `import-jobs` | Main work queue; messages are published by the API and consumed by the Worker |
| `import-jobs.dead-letters` | Messages that the Worker could not process and that exceeded RabbitMQ's delivery limits |

**Metrics to watch:**

| Metric | Healthy | Action needed |
|---|---|---|
| `Ready` | Low (close to 0) | Backlog building — check Worker health |
| `Unacked` | ≤ number of active Worker threads | Messages are stuck unacknowledged — check Worker logs |
| `Total` | Matches `Ready + Unacked` | — |

### Connections and Channels

Navigate to **Connections**. Expect at least two connections:

- One from the API (producer channel for `BasicPublish`)
- One from the Worker (consumer channel for `BasicConsume`)

If either is missing, restart the corresponding service.

---

## Dead-Letter Queue Investigation (RabbitMQ)

When the Worker fails to acknowledge a message and the broker's delivery limit is reached, the
message is moved to `import-jobs.dead-letters` via the dead-letter exchange `import-jobs.dlx`.

> **Note:** RabbitMQ dead-lettering is distinct from Ingestor's application-level `DeadLettered`
> job status. A job can reach `DeadLettered` status via either strategy. Inspect both the queue
> and the API to get the full picture.

### Steps

1. **Check for messages in the dead-letter queue**

   Management UI → **Queues** → `import-jobs.dead-letters` → **Get messages**.

   Each message contains a JSON body with `jobId`, `supplierCode`, and `importType`.

2. **Cross-reference with the job API**

   ```
   GET /api/imports/{jobId}
   ```

   Check `lastErrorCode`, `lastErrorMessage`, and `currentAttempt`.

3. **Read the full audit history**

   ```
   GET /api/imports/{jobId}/history
   ```

4. **Decide: requeue or discard**

   - If the root cause (infrastructure or data) has been resolved → see [Manual Requeue](#manual-requeue)
   - If the data is unrecoverable → no action needed; the job is already in `DeadLettered` state

5. **Remove the message from the dead-letter queue**

   Once the job has been handled via the API, purge or manually remove the dead-letter queue
   message via the Management UI to keep the queue clean.

---

## Switching Dispatch Strategies

Both the API and Worker must use the same strategy at all times. A mismatch (API publishes to
RabbitMQ, Worker polls the DB) will cause jobs to queue up without being processed.

### Switch to RabbitMQ

1. Ensure RabbitMQ is healthy: `docker compose ps rabbitmq`
2. Set `Dispatch__Strategy=RabbitMQ` on both `api` and `worker` services (environment variable
   or `appsettings.json`)
3. Restart both services:

   ```bash
   docker compose restart api worker
   ```

4. Submit a test job and confirm it transitions to `Succeeded`.

### Switch to Database

1. Set `Dispatch__Strategy=Database` on both services
2. Restart both services:

   ```bash
   docker compose restart api worker
   ```

3. Any messages remaining in the RabbitMQ `import-jobs` queue will not be consumed. If jobs
   were in-flight (published to RabbitMQ but not yet consumed), their `OutboxEntry` records
   were also written to the database and will be picked up by the Worker on the next poll cycle.

### Verify the active strategy

Check the Worker startup logs:

```bash
docker compose logs worker | grep -i "dispatch"
```

Look for a line confirming which dispatcher was registered (e.g. `Using DatabaseJobDispatcher`
or `Using RabbitMqJobDispatcher`).

---

## Investigating PartiallySucceeded Jobs

### Symptoms

- Job status is `PartiallySucceeded`
- `failedLines > 0`; `processedLines + failedLines = totalLines`
- Fewer `DeliveryItems` persisted than the total line count

### What it means

The job processed a file in chunks (batch mode). At least one chunk encountered a transient
error during the database write (e.g. connection timeout). The pipeline caught the error,
recorded the chunk as failed, and continued processing the remaining chunks.

### Steps

1. **Check job detail**

   ```
   GET /api/imports/{id}
   ```

   Note `processedLines`, `failedLines`, and `chunkSize`.

2. **Read the audit history**

   ```
   GET /api/imports/{id}/history
   ```

   Look for the `Processing → PartiallySucceeded` transition and any warning messages that
   accompany chunk failures.

3. **Check Worker logs around the processing time**

   ```bash
   docker compose logs worker | grep "<job-id>"
   ```

   Chunk failures are logged at `Warning` level:
   `Chunk {N}/{Total} failed for job {JobId}. Lines in chunk: {Count}.`

4. **Decide: requeue or accept**

   - **Requeue** if the infrastructure issue was transient and has resolved. The entire file
     will be reprocessed from scratch; already-persisted `DeliveryItems` from the original run
     are **not** deduplicated, so requeuing may produce duplicate items.
   - **Accept** if the failed lines are non-critical and the successfully processed items are
     sufficient for the business operation.

---

## Fly.io Deployment

> Applies when running Ingestor on Fly.io. The three apps are `ingestor-api`, `ingestor-worker`, and `ingestor-web`. See [ADR-021](adrs/021-cd-flyio.md) for the deployment design.

### Check Application Status

```bash
flyctl status --app ingestor-api
flyctl status --app ingestor-worker
flyctl status --app ingestor-web
```

### Stream Live Logs

```bash
flyctl logs --app ingestor-api
flyctl logs --app ingestor-worker
flyctl logs --app ingestor-web
```

---

### Data Protection Keys Lost After Machine Restart

**Symptoms**

- File upload button on the Imports page appears active but produces no response after a period of inactivity
- No network error visible in browser DevTools; the Blazor circuit connects successfully
- Server logs show antiforgery token validation failures

**Cause**

Fly.io machines can be stopped and restarted (scale-to-zero or machine replacement). Data Protection keys are persisted to a Fly.io volume mounted at `/data/keys`. If the volume is missing or detached, each restart generates new in-memory keys and tokens from the previous process become invalid.

**Fix**

1. Verify the volume exists:

   ```bash
   flyctl volumes list --app ingestor-web
   ```

   The volume `ingestor_web_keys` should be listed and `attached` to a machine. If it is missing, create it:

   ```bash
   flyctl volumes create ingestor_web_keys --region fra --size 1 --app ingestor-web
   ```

2. Confirm the environment variable is set (defined in `fly.web.toml`):

   ```
   DataProtection__KeysPath = "/data/keys"
   ```

3. Redeploy to attach the volume:

   ```bash
   flyctl deploy --config fly.web.toml --remote-only --ha=false
   ```

---

### Scale-to-Zero Cold Start Latency

**Symptoms**

- The first request after a period of inactivity takes noticeably longer (typically 5–15 seconds)

**Cause**

Expected behaviour. `fly.web.toml` sets `auto_stop_machines = 'stop'` and `min_machines_running = 1`, so one machine is always kept running. If latency still occurs, the machine may be restarting after a crash — check `flyctl status` and `flyctl logs`.

---

### Secret Update Not Taking Effect

**Symptoms**

- A secret was set via `flyctl secrets set` but the running machine still uses the old value

**Cause**

Fly.io secrets are applied on the next machine start. The currently running machine does not pick them up automatically.

**Fix**

Trigger a redeployment:

```bash
flyctl deploy --config fly.<app>.toml --remote-only --ha=false
```

Or restart the machine directly:

```bash
flyctl machines restart --app <app-name>
```

---

## Appendix

### Status Model Reference

```
Received → Parsing → Validating → Processing ──→ Succeeded
               │           │            │
               │           └────────────┴──→ ValidationFailed  (terminal, requeue allowed)
               │                        │
               │                        └──→ PartiallySucceeded  (terminal, batch jobs only)
               │
               └──→ ProcessingFailed ──→ (retry) ──→ Parsing
                                    └──→ (exhausted) ──→ DeadLettered  (terminal, requeue allowed)

DeadLettered       → Received  (manual requeue)
ValidationFailed   → Received  (manual requeue)
PartiallySucceeded → Received  (manual requeue, re-processes full file)
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
