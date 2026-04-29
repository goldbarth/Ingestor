# Changelog

All notable changes to this project will be documented in this file.
This project adheres to [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [3.2.0] — 2026-04-29

### Summary

V3.2 migrates the deployment target from Azure Container Apps to Fly.io and replaces Azure Blob Storage–based Data Protection key persistence with a local file system backed by a Fly.io persistent volume. All local Docker Compose and bare .NET setups are unaffected.

### Changed

#### CD pipeline — [ADR-021](docs/adrs/021-cd-flyio.md)
- `.github/workflows/cd.yml` — deployment target changed from Azure Container Apps to Fly.io; ACR push and `az containerapp update` steps replaced by `flyctl deploy` for all three apps (`ingestor-api`, `ingestor-worker`, `ingestor-web`)
- Authentication changed from OIDC / Workload Identity Federation to a single `FLY_API_TOKEN` secret
- Image build moved to Fly.io remote builder — no external container registry required
- EF Core migrations now run as the first CD step before any application is deployed
- Machine-replace strategy: existing machines are destroyed before each deployment to stay within free-tier limits (`--ha=false`)

#### Data Protection key storage — [ADR-021](docs/adrs/021-cd-flyio.md)
- `Ingestor.Web/Program.cs` — `PersistKeysToAzureBlobStorage` replaced by `PersistKeysToFileSystem`; configuration key changed from `DataProtection:StorageConnectionString` to `DataProtection:KeysPath`
- `fly.web.toml` — persistent volume `ingestor_web_keys` mounted at `/data/keys`; `DataProtection__KeysPath` set to `/data/keys`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs` NuGet package no longer required

### Superseded

- [ADR-019](docs/adrs/019-data-protection-azure-blob-storage.md) — Azure Blob Storage key persistence
- [ADR-020](docs/adrs/020-ci-cd-github-actions-acr-oidc.md) — CI/CD with ACR and OIDC

### Migration notes

**Local Docker Compose and bare .NET** setups are not affected. `DataProtection:KeysPath` is opt-in; when absent the application falls back to default in-memory key storage.

**Fly.io deployments:** ensure the `ingestor_web_keys` volume exists before deploying `ingestor-web`. The CD pipeline creates it automatically if missing. The `FLY_API_TOKEN` secret must be set in GitHub Secrets; the three Azure credential secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) can be removed.

---

## [3.1.0] — 2026-03-31

### Summary

V3.1 automates deployment to Azure Container Apps via a GitHub Actions CD pipeline and makes the import-job retry limit configurable via `appsettings.json`.

### Added

#### CD pipeline (GitHub Actions) — [ADR-020](docs/adrs/020-ci-cd-github-actions-acr-oidc.md)
- `.github/workflows/cd.yml` — triggers automatically after a successful CI run on `main`; builds and pushes Docker images for API, Worker, and Web to Azure Container Registry (ACR), then updates all three Container Apps
- Authentication via OIDC (Workload Identity Federation) — GitHub Secrets hold only non-sensitive IDs (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`); no passwords stored
- Images tagged with the full commit SHA (`head_sha`) — every running revision is traceable to the exact git commit that produced it
- Container Apps pull images via System-assigned Managed Identity (`AcrPull` role) — no registry credentials on the Container App side
- `workflow_run` gate ensures CD cannot run on broken code

#### Configurable max attempts for import jobs (#171)
- `ImportJobOptions` — new options class with `MaxAttempts`; registered via `IOptions<ImportJobOptions>` in both API and Worker hosts
- `CreateImportJobHandler` and the Worker processing path read `MaxAttempts` from configuration instead of a hardcoded constant
- Default value added to `appsettings.json` in both `Ingestor.Api` and `Ingestor.Worker`

### Changed

- README updated with CI/CD pipeline documentation and Azure Container Registry setup notes

---

## [3.0.0] — 2026-03-30

### Summary

V3 adds a complete Blazor Server web UI and cloud deployment support for Azure Container Apps. The backend is unchanged; all V2 behaviour is preserved by default.

### Added

#### Web UI (Blazor Server) — [ADR-018](docs/adrs/018-blazor-server-web-ui.md)
- `Ingestor.Web` — new Blazor Server project with three pages:
  - **Dashboard** — real-time job and processing metrics
  - **Imports** — file upload with live job list and status tracking
  - **Dead Letters** — list of failed jobs with requeue capability
- `IngestorApiClient` — typed HTTP client for API communication
- Dockerfile for `Ingestor.Web` (multi-stage build, non-root user)
- CI pipeline extended: Docker build for all three images runs as final quality gate
- `docker-compose.yml` — `web` service added (port `8202`, `http://localhost:8202`)

#### Azure Container Apps deployment — [ADR-019](docs/adrs/019-data-protection-azure-blob-storage.md)
- Data Protection keys persisted to Azure Blob Storage via `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- Prevents antiforgery/circuit failures after scale-to-zero container restarts
- Opt-in via `DataProtection:StorageConnectionString`; no impact on local or non-Azure deployments

### Migration notes

**Existing deployments** (local Docker Compose, bare .NET) are not affected. `DataProtection:StorageConnectionString` is optional and absent by default.

**Azure Container Apps:** create a Storage Account, a `dataprotection` blob container, and store the connection string as a Container Apps secret. Reference it via `DataProtection__StorageConnectionString=secretref:<secret-name>`. See the [runbook](docs/runbook.md#azure-container-apps-deployment) for the step-by-step procedure.

---

## [2.0.0] — 2026-03-28

### Summary

V2 introduces a config-switchable dispatch strategy (database queue or RabbitMQ), chunk-based batch import for large files, and a data-driven benchmark comparison of both dispatch strategies. All V1 behaviour is preserved by default; no migration steps are required for existing deployments.

### Added

#### Dispatcher abstraction (M6)
- `IJobDispatcher` interface with `DispatchAsync` and `AcknowledgeAsync` operations — decouples job dispatch from the outbox implementation
- `DatabaseJobDispatcher` — preserves V1 outbox-based dispatch as an explicit implementation
- `Dispatch:Strategy` configuration key — switches between `"Database"` (default) and `"RabbitMQ"` at runtime without code changes
- `IAfterSaveCallbackRegistry` — allows post-commit side effects (e.g. broker publish) to be registered by infrastructure and executed after the transaction commits ([ADR-017](docs/adrs/017-rabbitmq-post-commit-publish.md))

#### RabbitMQ integration (M7)
- `RabbitMqJobDispatcher` — publishes job messages to `import-jobs` queue after transaction commit
- `RabbitMqWorker` (hosted service) — consumes messages from `import-jobs`, hands off to the existing pipeline, and acknowledges on success
- Dead-letter exchange `import-jobs.dlx` and queue `import-jobs.dead-letters` — captures messages that cannot be delivered
- `RabbitMqConnectionManager` — handles initial connection with configurable retry interval and reconnect on channel failure
- RabbitMQ added to `docker-compose.yml` with Management UI (`rabbitmq:3-management`, ports `5672` and `15672`)
- RabbitMQ Management UI reachable at `http://localhost:15672`
- [ADR-014](docs/adrs/014-rabbitmq-integration.md): RabbitMQ integration design

#### Throughput benchmark (M8)
- `Ingestor.Benchmarks` project with BenchmarkDotNet
- Three scenarios: 100 jobs sequential, 1 000 jobs sequential, 100 jobs with 2 concurrent workers
- [ADR-015](docs/adrs/015-benchmark-results.md): measured results and recommendation (retain DB queue as default; switch to RabbitMQ when submission rate or latency requirements demand it)

#### Batch import (M9)
- `LineChunker` — splits parsed lines into fixed-size chunks (`Batch:ChunkSize`, default 500)
- Files exceeding `ChunkSize` lines are processed as batch jobs; each chunk is committed atomically
- `ImportJob` batch-tracking fields: `isBatch`, `totalLines`, `processedLines`, `failedLines`, `chunkSize`
- `PartiallySucceeded` terminal status — set when at least one chunk fails; remaining chunks continue
- `ImportJob.InitializeBatch`, `RecordChunkProcessed`, `RollbackChunkProcessed`, `RecordChunkFailed` — domain methods for per-chunk progress accounting
- Invariant enforced: `processedLines + failedLines == totalLines`; `DeliveryItems.Count == processedLines`
- `EfUnitOfWork.SaveChangesAsync` detaches all `EntityState.Added` entities on exception, preventing re-save of failed chunk items in the catch-block save
- Integration tests: batch import with 10 000 lines (happy path, partial failure via fault injection, validation failure at scale)
- [ADR-016](docs/adrs/016-batch-import-strategy.md): chunk strategy and partial failure semantics

#### Documentation (M10)
- README updated to reflect V2 capabilities, dispatch strategies, batch import, quick start with `.env`, and RabbitMQ endpoints
- Runbook extended with RabbitMQ connection issues, queue inspection, dead-letter investigation, strategy switching, and `PartiallySucceeded` investigation procedures
- This CHANGELOG

### Fixed

- **EF tracking bug in partial failure path** — when `SaveChangesAsync` failed mid-chunk (e.g. via injected timeout), EF Core retained chunk entities as `Added`. The catch block's subsequent `SaveChangesAsync` would silently persist those entities anyway, causing `FailedLines` and `DeliveryItems.Count` to be inconsistent. Fixed by detaching `Added` entities in `EfUnitOfWork` and rolling back the optimistic `ProcessedLines` increment via `RollbackChunkProcessed` ([#142](https://github.com/goldbarth/Ingestor/issues/142))

### Changed

- `docker-compose.yml` default dispatch strategy changed from `Database` to `RabbitMQ` (RabbitMQ is now included in the compose stack)
- `ImportJob` domain entity extended with nullable batch-tracking properties; all new fields are nullable and have no effect on non-batch jobs
- ADR count: 12 (V1) → 17 (V2); all ADRs in [`docs/adrs/`](docs/adrs/)

### Migration notes

**Existing deployments** using the database dispatch strategy require no changes. `Dispatch:Strategy` defaults to `"Database"`.

**Database schema** — new columns on `import_jobs` (`is_batch`, `total_lines`, `processed_lines`, `failed_lines`, `chunk_size`) are added by EF Core migrations. Migrations are applied automatically on startup or via:

```bash
dotnet ef database update --project src/Ingestor.Infrastructure
```

**RabbitMQ** — only required when `Dispatch:Strategy = "RabbitMQ"`. Add `RABBITMQ_PASSWORD` to your `.env` file and run `docker compose up`.

---

## [1.0.0] — 2026-03-18

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
