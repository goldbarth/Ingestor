<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/ingestor-wordmark-dark.svg">
  <img alt="Ingestor" src="docs/assets/ingestor-wordmark-light.svg" width="360">
</picture>

[![Release](https://img.shields.io/github/v/release/goldbarth/Ingestor?label=release&color=14807A&style=flat-square&labelColor=26201A)](https://github.com/goldbarth/Ingestor/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/goldbarth/Ingestor/ci.yml?branch=main&label=CI&style=flat-square&labelColor=26201A)](https://github.com/goldbarth/Ingestor/actions/workflows/ci.yml)
[![CD](https://img.shields.io/github/actions/workflow/status/goldbarth/Ingestor/cd.yml?branch=main&label=CD&style=flat-square&labelColor=26201A)](https://github.com/goldbarth/Ingestor/actions/workflows/cd.yml)
[![ADRs](https://img.shields.io/badge/ADRs-21-C68A35?style=flat-square&labelColor=26201A)](docs/adrs)
[![License: MIT](https://img.shields.io/badge/license-MIT-C68A35?style=flat-square&labelColor=26201A)](LICENSE)

![.NET 10](https://img.shields.io/badge/.NET-10-14807A?style=flat-square&logo=dotnet&logoColor=2DD4BF&labelColor=26201A)
![PostgreSQL](https://img.shields.io/badge/-PostgreSQL-14807A?style=flat-square&logo=postgresql&logoColor=2DD4BF&labelColor=26201A)
![RabbitMQ](https://img.shields.io/badge/-RabbitMQ-14807A?style=flat-square&logo=rabbitmq&logoColor=2DD4BF&labelColor=26201A)
![Blazor](https://img.shields.io/badge/-Blazor-14807A?style=flat-square&logo=blazor&logoColor=2DD4BF&labelColor=26201A)

</div>

Ingestor is a private .NET proof of concept for asynchronous file imports.
It explores how a database-backed outbox, retries and dead-letter handling work together when imports fail.

Suppliers upload delivery advice files (CSV or JSON) through an API.
A background worker parses, validates and stores them, retrying infrastructure errors and dead-lettering jobs that cannot complete.
A Blazor Server UI shows job status and failures and lets operators requeue failed jobs.
Fleetholm Logistics is the fictional company used as the domain context.

## Why I built it

I wanted to work through what happens when an import fails partway through processing, a worker stops unexpectedly, or the same file is submitted twice.
The project brings these cases together in a runnable application, with a database-backed outbox, retry handling, dead-letter management and an alternative RabbitMQ dispatch strategy.

It also became a way to gain practical experience with cloud deployment, first on Azure and later with automated deployment through GitHub Actions.

## My contribution and use of coding agents

Coding agents wrote the application code.
The architecture decisions recorded in the [ADRs](docs/adrs/) are mine, in the sense that none of them went into the project without my review.
I tried out the processing flows in practice, including failure cases such as a worker stopping mid-run and the same file being uploaded twice.
I also set up the Azure deployment and the automated deployment pipeline myself.

## How it works

Three processes share one PostgreSQL database: the API, the worker and the web UI.
The API stores the uploaded file and creates a job.
The worker picks the job up, either by polling an outbox table or by consuming a RabbitMQ queue, and runs the pipeline.

```
 ┌─────────────────────────┐
 │      Ingestor.Web       │
 │  Blazor Server UI       │
 │  Dashboard, Imports,    │
 │  Dead letters           │
 └────────────┬────────────┘
              │ HTTP
              ▼
 ┌─────────────────────────┐                      ┌─────────────────────────┐
 │      Ingestor.Api       │                      │     Ingestor.Worker     │
 │                         │──── DB strategy ────►│  Poll outbox_entries    │
 │  POST /api/imports      │                      │                         │
 │  GET  /api/imports      │──── MQ strategy ────►│  Consume import-jobs    │
 │  GET  /api/imports/{id} │                      │                         │
 │  POST .../{id}/requeue  │                      │  Parse → Validate →     │
 │  GET  /api/metrics/...  │                      │  Process (chunked)      │
 └────────────┬────────────┘                      └────────────┬────────────┘
              │                                                │
              └───────────────────────┬────────────────────────┘
                                      ▼
                    ┌───────────────────────────────────┐
                    │            PostgreSQL             │
                    ├───────────────────────────────────┤
                    │  import_jobs                      │
                    │  import_payloads                  │
                    │  outbox_entries                   │
                    │  delivery_items                   │
                    │  import_attempts                  │
                    │  dead_letter_entries              │
                    │  audit_events                     │
                    └───────────────────────────────────┘

                    ┌───────────────────────────────────┐
                    │  RabbitMQ  (Strategy = RabbitMQ)  │
                    ├───────────────────────────────────┤
                    │  import-jobs        (work queue)  │
                    │  import-jobs.dlx    (DLX)         │
                    │  import-jobs.dead-letters         │
                    └───────────────────────────────────┘
```

### Upload

`POST /api/imports` accepts a multipart form with `supplierCode`, `importType` (`CsvDeliveryAdvice` or `JsonDeliveryAdvice`) and a `text/csv` or `application/json` file of up to 10 MB.
The raw file is stored in `import_payloads`, separate from the job record.
The API answers `201 Created` with the job ID; processing happens later in the worker.

### Pipeline

The worker moves each job through parsing, validation and processing and records every status change.
Validation checks all lines before anything is stored, so a single invalid line fails the whole job with `ValidationFailed`.
Parse and validation errors are treated as data problems and are not retried.

```
Received ─► Parsing ─► Validating ─► Processing ─┬─► Succeeded
               │            │             │      └─► PartiallySucceeded  (batch jobs only)
               │            │             │
               ├────────────┴─────────────┼────► ValidationFailed  (parse or validation errors)
               │                          │
               └──────────────────────────┴────► ProcessingFailed  (exception during the run)
                                                   ├─► Parsing       (retry, attempts left)
                                                   └─► DeadLettered  (attempts exhausted)

Manual requeue via API:  DeadLettered ─► Received,  ValidationFailed ─► Received
```

The allowed transitions are defined in `ImportJobWorkflow`.
An unlisted transition throws a `DomainException`.

### Batch import

Files with more lines than `Batch:ChunkSize` (default 500) are processed as batch jobs.
Each chunk is saved in its own database call.
If saving a chunk fails, its lines are counted as failed and the remaining chunks continue.
A batch job with at least one failed chunk ends as `PartiallySucceeded`, and failed chunks are not retried automatically.
Files up to the chunk size are saved in a single call and either succeed or fail as a whole.

The job resource shows the breakdown:

```json
{
  "isBatch": true,
  "totalLines": 10000,
  "chunkSize": 500,
  "processedLines": 9500,
  "failedLines": 500
}
```

See [ADR-016](docs/adrs/016-batch-import-strategy.md) for the design rationale.

## Dispatch strategies

`Dispatch:Strategy` selects how the API hands jobs to the worker.
API and worker must use the same value.
An unknown value logs a warning and falls back to `Database`.

### Database (default)

The job, its payload and an `OutboxEntry` are written in the same `SaveChanges` call, so they commit together.
The worker polls `outbox_entries` every `Worker:PollingIntervalSeconds` (default 5) and claims one entry at a time with `FOR UPDATE SKIP LOCKED`, so concurrent workers do not claim the same entry.
No infrastructure beyond PostgreSQL is needed.

### RabbitMQ

The API publishes a message to the `import-jobs` queue after the database transaction has committed ([ADR-017](docs/adrs/017-rabbitmq-post-commit-publish.md)).
The worker consumes with manual acknowledgement and a prefetch of 1.
Messages for failed attempts are rejected into the dead-letter exchange `import-jobs.dlx`; retry decisions stay in the worker ([ADR-014](docs/adrs/014-rabbitmq-integration.md)).

This path has no outbox.
If publishing fails after the commit, the job stays in `Received` without a queue message, and re-uploading the file returns the existing job instead of dispatching it again.
ADR-017 documents this as an accepted limitation.

## Failure handling and its limits

**Idempotent upload.**
The idempotency key is the supplier code plus a SHA-256 hash of the file content, and the database enforces it with a unique index.
Uploading the same file for the same supplier again returns `200 OK` with the existing job instead of creating a new one.
If that job is dead-lettered, the API returns `409 Conflict` and points to the requeue endpoint.
The same content from a different supplier creates a new job.
If two identical uploads race, the unique index prevents a second job, but the losing request fails with a server error instead of returning the existing job.

**Retry with backoff.**
Exceptions that escape the pipeline are classified by `ExceptionClassifier`.
Transient PostgreSQL errors and timeouts are retried up to `Import:MaxAttempts` (default 3); all other exceptions count as permanent.
With the database strategy, each retry is scheduled with a delay of 4^(n-1) seconds after the n-th failed attempt (1 s, 4 s, ...).
With RabbitMQ, the retry message is published immediately without a delay.

**Dead-lettering and requeue.**
When a job fails permanently or runs out of attempts, the worker writes a `dead_letter_entries` row with the error, supplier, import type and attempt count, and sets the job to `DeadLettered`.
`POST /api/imports/{id}/requeue` moves a `DeadLettered` or `ValidationFailed` job back to `Received`, resets the attempt counter and dispatches it again.
The job is reprocessed from the stored payload, so a `ValidationFailed` job will fail again unless the validation rules have changed.
Other statuses return `409 Conflict`.

**Stale-lock recovery (database strategy).**
On every poll, outbox entries that have been in `Processing` for longer than `Worker:StaleLockTimeoutSeconds` (default 300) are reset to `Pending` ([ADR-012](docs/adrs/012-stale-outbox-lock-recovery.md)).
This frees entries left behind by a crashed worker.
The integration test covers a job that never got past `Received`.
A job whose status had already advanced cannot restart at `Parsing`, because the status model does not allow that transition.
A worker that is still running after the timeout can also have its entry handed to another worker.

**Audit trail.**
Every status change after creation writes an `audit_events` row with the old and new status, the trigger (worker or API), a timestamp and an optional comment.
`GET /api/imports/{id}/history` returns it.

**Result pattern.**
Application handlers return `Result<T>` for expected outcomes such as not found, conflict or validation errors, and the API maps them to ProblemDetails responses ([ADR-010](docs/adrs/010-result-pattern.md)).
The pipeline reports parse and validation failures as results as well.
Infrastructure exceptions and `DomainException`s for invalid transitions still propagate to the worker, which classifies them.

## Tests and recorded results

The repository contains these test projects.
CI runs the unit, architecture and integration tests on pushes and pull requests to `main`; the CI badge above shows the latest run.

| Project | What it covers |
|---|---|
| `Ingestor.Tests.Unit` | Parsers, validator, status transitions, retry delay, chunking, batch partial failure, job creation, requeue, dead-letter entries, exception classification |
| `Ingestor.Tests.Integration` | Pipeline against a real PostgreSQL via Testcontainers: successful import, invalid file, retry from `ProcessingFailed`, dead-letter persistence, a 10,000-line batch with and without an injected chunk fault, database timeouts and stale-lock recovery, and an end-to-end run through RabbitMQ |
| `Ingestor.Tests.Architecture` | Project exists and references NetArchTest, but contains no tests yet; layer boundaries are currently enforced by project references only |
| `Ingestor.Benchmarks` | BenchmarkDotNet comparison of the two dispatch strategies |

The integration tests set the retry and dead-letter states up directly.
The worker's own decision path from a failed run to `DeadLettered` is not covered by an automated test.

The batch fault test injects a failure into the first chunk of a 10,000-line file and expects `PartiallySucceeded` with 9,500 processed lines, 500 failed lines and 9,500 stored delivery items.

[ADR-015](docs/adrs/015-benchmark-results.md) records a benchmark I ran on my own machine (Ryzen 7 5800X, Windows 11) with single-line jobs, a polling interval of 0 and 5 measured iterations.
In that run, RabbitMQ processed about 165 to 174 jobs per second and the database strategy about 43 to 57, roughly 3 times faster at 100 jobs and 4 times at 1,000 jobs.
The ADR treats the numbers as orientation rather than statistically robust results and recommends keeping the database strategy as the default at the project's scale.

```bash
dotnet test Ingestor.slnx -c Release
```

The integration tests need Docker for Testcontainers.

## Getting started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [Docker](https://docs.docker.com/get-docker/).

### 1. Create a `.env` file

The repository contains no template, so create `.env` in the project root:

```bash
# Required
POSTGRES_PASSWORD=<your-db-password>
RABBITMQ_PASSWORD=<your-rabbitmq-password>
CONNECTION_STRING=Host=postgres;Port=5432;Database=ingestor;Username=ingestor;Password=<your-db-password>

# Optional, defaults shown
POSTGRES_DB=ingestor
POSTGRES_USER=ingestor
RABBITMQ_USER=guest
DISPATCH_STRATEGY=Database   # or RabbitMQ, applies to API and worker
```

### 2. Start the stack

```bash
docker compose up
```

This starts PostgreSQL, RabbitMQ, the API, the worker and the web UI, plus a local observability stack (OpenTelemetry Collector, Prometheus, Tempo, Loki and Grafana).
RabbitMQ starts in either strategy but is only used with `DISPATCH_STRATEGY=RabbitMQ`.
The API applies pending database migrations on startup.

| Service | URL |
|---|---|
| Web UI | http://localhost:8202 |
| API | http://localhost:8200 |
| OpenAPI document (Development only) | http://localhost:8200/openapi/v1.json |
| API health | http://localhost:8200/health |
| Worker health | http://localhost:8201/health |
| RabbitMQ Management UI | http://localhost:15672 |
| Grafana | http://localhost:3000 |

### 3. Upload a file

Upload a file through the web UI, or with `curl`:

```bash
cat > delivery.csv <<'EOF'
ArticleNumber,ProductName,Quantity,ExpectedDate,SupplierRef
ART-001,Sofa Milano,10,2030-01-15T00:00:00Z,REF-001
ART-002,Chair Stockholm,5,2030-02-01T00:00:00Z,REF-002
EOF

curl -F supplierCode=SUP-001 \
     -F importType=CsvDeliveryAdvice \
     -F "file=@delivery.csv;type=text/csv" \
     http://localhost:8200/api/imports
```

`ExpectedDate` must lie in the future, and `Quantity` must be greater than 0.
Follow the job with `GET /api/imports/{id}` and `GET /api/imports/{id}/history`.
The [runbook](docs/runbook.md) covers the Grafana dashboards, dead-letter investigation and switching strategies.

## API endpoints

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/imports` | Upload a file and create a job |
| `GET` | `/api/imports` | List jobs, cursor-paginated, filterable by `status` |
| `GET` | `/api/imports/{id}` | Job detail with status, attempts, last error and batch progress |
| `GET` | `/api/imports/{id}/history` | Audit trail of status changes |
| `POST` | `/api/imports/{id}/requeue` | Requeue a `DeadLettered` or `ValidationFailed` job |
| `GET` | `/api/metrics/jobs` | Job counts by status |
| `GET` | `/api/metrics/processing` | Average duration and success rate |

## Deployment history

I first deployed the application in v3.0.0 to Azure Container Apps, pushing images to Docker Hub and updating the Container Apps by hand.
In v3.1.0 I automated this with GitHub Actions: images were pushed to Azure Container Registry, the pipeline authenticated through OIDC, and the Container Apps pulled images through a managed identity.
The web UI stored its Data Protection keys in Azure Blob Storage.
[ADR-019](docs/adrs/019-data-protection-azure-blob-storage.md) and [ADR-020](docs/adrs/020-ci-cd-github-actions-acr-oidc.md) document this setup; both are now superseded.

Since v3.2.0, the repository targets Fly.io ([ADR-021](docs/adrs/021-cd-flyio.md)).
After a successful CI run on `main`, the CD workflow applies EF Core migrations to the database configured in the `DB_CONNECTION_STRING` secret and deploys `ingestor-api`, `ingestor-worker` and `ingestor-web` with the Fly.io remote builder.
The web UI keeps its Data Protection keys on a Fly.io volume.
The Fly.io configuration in the repository sets no dispatch strategy, so the `Database` default applies, and it does not include RabbitMQ.
The configuration describes how the project is deployed; it does not show whether the apps are currently running.

## Layer structure

```
Api     ──► Application, Infrastructure, Contracts
Worker  ──► Application, Infrastructure
Web     ──► Contracts
Infrastructure ──► Application, Domain
Application    ──► Domain
Domain, Contracts ──► (nothing)
```

| Layer | Responsibility |
|---|---|
| **Domain** | Entities, status model, validation rules, domain errors |
| **Application** | Use-case handlers, import pipeline, parsers, repository abstractions |
| **Infrastructure** | EF Core and PostgreSQL, outbox repository, both dispatchers, RabbitMQ consumer |
| **Contracts** | Versioned HTTP request and response DTOs |
| **Api** | Minimal API endpoints, ProblemDetails mapping |
| **Worker** | Polling loop for the database strategy, retry and dead-letter decisions |
| **Web** | Blazor Server UI, talks to the API over HTTP |

## Design decisions

| Decision | Where | ADR |
|---|---|---|
| Database-backed queue instead of a broker by default | `OutboxRepository` | [ADR-001](docs/adrs/001-db-queue-over-broker.md) |
| Idempotency key from supplier and content hash | `IdempotencyKeyComputer` | [ADR-002](docs/adrs/002-idempotency-strategy.md) |
| `FOR UPDATE SKIP LOCKED` for claiming outbox entries | `OutboxRepository.ClaimNextAsync` | [ADR-003](docs/adrs/003-pessimistic-locking.md) |
| Raw payload stored separately from the job | `ImportPayload` | [ADR-004](docs/adrs/004-raw-payload-persistence.md) |
| Transient vs. permanent error classification | `ExceptionClassifier` | [ADR-005](docs/adrs/005-error-classification-transient-vs-permanent.md) |
| Explicit status model with enforced transitions | `ImportJobWorkflow` | [ADR-006](docs/adrs/006-status-model-design.md) |
| OpenTelemetry tracing per pipeline step | `IngestorActivitySource` | [ADR-007](docs/adrs/007-opentelemetry-tracing.md) |
| Result pattern in the application layer | `Result<T>` | [ADR-010](docs/adrs/010-result-pattern.md) |
| Timeout-based recovery of stale outbox locks | `OutboxRepository.RecoverStaleAsync` | [ADR-012](docs/adrs/012-stale-outbox-lock-recovery.md) |
| Dispatcher abstraction with configuration switch | `IJobDispatcher` | [ADR-013](docs/adrs/013-dispatcher-abstraction.md) |
| RabbitMQ topology and retry ownership | `RabbitMqJobDispatcher`, `RabbitMqWorker` | [ADR-014](docs/adrs/014-rabbitmq-integration.md) |
| Database queue vs. RabbitMQ benchmark | `Ingestor.Benchmarks` | [ADR-015](docs/adrs/015-benchmark-results.md) |
| Chunked batch processing with partial failures | `LineChunker`, `BatchOptions` | [ADR-016](docs/adrs/016-batch-import-strategy.md) |
| RabbitMQ publish after commit | `IAfterSaveCallbackRegistry` | [ADR-017](docs/adrs/017-rabbitmq-post-commit-publish.md) |
| Blazor Server as web frontend | `Ingestor.Web` | [ADR-018](docs/adrs/018-blazor-server-web-ui.md) |
| CI/CD with GitHub Actions and Fly.io | `.github/workflows/cd.yml`, `fly.*.toml` | [ADR-021](docs/adrs/021-cd-flyio.md) |

All 21 ADRs, including UUID v7 IDs, cursor pagination and strongly-typed IDs, are in [`docs/adrs/`](docs/adrs/).

## Tech stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 (SDK pinned in `global.json`) |
| API | ASP.NET Core Minimal API, OpenAPI |
| Web UI | Blazor Server |
| Persistence | EF Core 10, Npgsql, PostgreSQL (17 in Docker Compose) |
| Background processing | .NET `BackgroundService` |
| Message broker | RabbitMQ 3 via `RabbitMQ.Client` (optional) |
| Logging | Serilog (compact JSON to console), OTLP export |
| Tracing and metrics | OpenTelemetry; locally OTel Collector, Tempo, Prometheus, Loki, Grafana |
| Testing | xUnit, FluentAssertions, Testcontainers, BenchmarkDotNet |
| Containers | Docker, Docker Compose |
| CI/CD | GitHub Actions, Fly.io |

## Documentation

- [V1 scope](docs/en/scope-v1-en.md)
- [V2 scope](docs/en/scope-v2-en.md)
- [Architecture Decision Records](docs/adrs/)
- [Operational runbook](docs/runbook.md)
- [Changelog](CHANGELOG.md)

## License

[MIT](LICENSE)
