# Changelog

## v1.0.0

### Summary

Ingestor 1.0.0 is the first complete release of this project.
It covers the full import lifecycle — from file upload through parsing, validation,
and processing — backed by a database-driven outbox pattern, a strict domain state
machine, and a fault-tolerant background worker with retry, dead-letter, and
stale-lock recovery.

### Highlights

- Database-backed outbox pattern — reliable job dispatch without a message broker
- Strict job state machine with domain-enforced transition rules
- Retry with exponential backoff and dead-letter handling
- Stale-lock recovery for worker crashes and DB timeouts
- Idempotent file ingestion via SHA-256 content + supplier hash
- Result pattern — no unhandled exceptions cross application boundaries
- Full audit trail for every job lifecycle event
- OpenTelemetry tracing across all pipeline steps
- Structured logging with Serilog and per-job correlation
- Health checks for API and Worker hosts
- Job metrics endpoints (counts by status, processing duration)
- Integration tests with Testcontainers against a real PostgreSQL instance

### Demo Notes

#### Suggested demo flow

1. Start all services: `docker compose up`
2. Open the interactive API docs at `http://localhost:8200/scalar`
3. Upload a delivery advice CSV via `POST /api/imports`
4. Poll `GET /api/imports/{id}` and watch the status progress through the lifecycle
5. Upload the same file again — observe the idempotency response
6. Upload an invalid CSV — observe `ValidationFailed` with a structured error
7. Inspect `GET /api/imports/{id}/history` for the full audit trail
8. Check `GET /api/metrics/jobs` for a live count by status
9. Check `GET /health` on both API and Worker

#### What to pay attention to

- The outbox entry is written in the same transaction as the job — no job is ever lost between upload and processing
- The worker uses `FOR UPDATE SKIP LOCKED` — safe to run multiple instances concurrently
- State transitions are validated in the domain layer — the application layer cannot put a job into an invalid state
- Transient failures (e.g. DB timeout) are retried with backoff; permanent failures (e.g. parse error) fail immediately
- Worker crashes leave entries in `Processing` — `RecoverStaleAsync` resets them on the next poll cycle without manual intervention
- Every status change produces an `AuditEvent` — the full history is always reconstructible

### Included scope

- M1: Solution structure, Docker Compose, EF Core, domain entities, API endpoints
- M2: CSV and JSON parsers, delivery data validator, processing pipeline, status transitions
- M3: Background worker, retry with exponential backoff, dead-letter, idempotency, attempt audit
- M4: OpenTelemetry tracing, Serilog structured logging, health checks, metrics endpoints, audit history endpoint
- M5: Fault injection tests, edge case hardening, CI/CD pipeline, ADRs, stale-lock recovery, README

### V1 scope boundaries

The following are deliberately out of scope for V1 and are addressed in V2:

- No message broker — job dispatch uses the database-backed outbox only
- No batch import — one file produces one job; large-file chunking is a V2 feature
- No authentication or authorization
- Single-tenant design (no per-tenant isolation beyond the idempotency key)
- No file streaming — files are uploaded and stored in full before processing begins

### Links

- [Repository](https://github.com/goldbarth/Ingestor)
- [V1 Scope](docs/en/scope-v1-en.md)
- [Architecture Decision Records](docs/adrs/)
- [Operational Runbook](docs/runbook.md)
