# Ingestor — Fleetholm Logistics

<p>
  <img src="https://img.shields.io/github/v/release/goldbarth/Ingestor"/>
  <a href="https://github.com/goldbarth/Ingestor/actions/workflows/ci.yml">
    <img src="https://github.com/goldbarth/Ingestor/actions/workflows/ci.yml/badge.svg" alt="CI" />
  </a>
</p>

A production-grade .NET 10 application for reliable asynchronous import processing. Files are received, validated, processed in configurable chunks, and tracked — with structured error handling, automatic retries, full audit trails, and a config-switchable dispatch strategy (database queue or RabbitMQ). Includes a Blazor Server web UI for operational monitoring, file uploads, and dead-letter management.

> Fleetholm Logistics is a fictional company used as the domain context for this project.

---

## Core capabilities

- **Outbox pattern** — jobs are enqueued via a database table; no distributed transactions required
- **Config-switchable dispatcher** — run with the database queue or RabbitMQ without code changes (`Dispatch:Strategy`)
- **Batch import** — large files are split into fixed-size chunks; each chunk is persisted atomically; partial failures produce `PartiallySucceeded` rather than losing all work
- **Strict state machine** — all job status transitions are validated at the domain layer; invalid transitions throw
- **Retry with exponential backoff** — transient failures are retried up to a configurable maximum; permanent failures fail immediately
- **Dead-letter & manual requeue** — exhausted jobs are snapshotted and can be requeued via API
- **Stale-lock recovery** — `Processing` entries orphaned by worker crashes are automatically recovered on the next poll cycle
- **Idempotency** — duplicate uploads are detected by SHA-256 content + supplier hash; no double processing
- **Result pattern** — no exceptions cross application layer boundaries; all outcomes are explicit `Result<T>` values
- **Full audit trail** — every status transition is recorded with trigger, timestamp, and context

---

## System architecture

Three independently deployable processes share a PostgreSQL database. The dispatch path between API and Worker is configurable at runtime.

```
 ┌─────────────────────────┐
 │     Ingestor.Web        │
 │                         │
 │  Dashboard              │──── HTTP ──────────────────────────┐
 │  Imports (upload)       │                                    │
 │  Dead Letters           │                                    │
 └─────────────────────────┘                                    │
                                                                ▼
 ┌─────────────────────────┐                      ┌─────────────────────────┐
 │      Ingestor.Api       │                      │    Ingestor.Worker      │
 │                         │                      │                         │
 │  POST /api/imports      │──── DB strategy ────►│  BackgroundService      │
 │  GET  /api/imports      │                      │  Poll outbox_entries    │
 │  GET  /api/imports/{id} │──── MQ strategy ────►│  Consume import-jobs    │
 │  POST /api/imports/{id} │          │           │                         │
 │       /requeue          │          │           │  Parse → Validate       │
 │  GET  /api/metrics      │          │           │  → Process (chunked)    │
 └───────────┬─────────────┘          │           └───────────┬─────────────┘
             │                        │                       │
             └────────────────────────┼───────────────────────┘
                                      │
                    ┌─────────────────▼─────────────────┐
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

---

## Job lifecycle

```
Received ──→ Parsing ──→ Validating ──→ Processing ──→ Succeeded
                │              │               │
                ├──────────────┴───→ ValidationFailed
                │                              │
                │                       PartiallySucceeded  (batch jobs with chunk failures)
                │
                └──→ ProcessingFailed ──→ (retry) ──→ Parsing
                                    └──→ (exhausted) ──→ DeadLettered
                                                               │
                                                         (requeue) ──→ Received
```

`PartiallySucceeded` is a terminal status for batch jobs where at least one chunk encountered a transient processing error. Successfully processed chunks remain persisted; `processedLines` and `failedLines` on the job record show the exact breakdown.

Each transition is enforced by `ImportJobWorkflow`. Attempting an unlisted transition throws a `DomainException`.

---

## Layer structure

```
Worker  ──┐
Api     ──┼──→  Application  ──→  Domain
          └──→  Infrastructure ──→  Application, Domain

Domain          →  (nothing)
Contracts       →  (nothing)
```

| Layer | Responsibility |
|---|---|
| **Domain** | Entities, value objects, state machine, domain errors |
| **Application** | Use-case handlers, pipeline orchestration, repository abstractions |
| **Infrastructure** | EF Core, PostgreSQL, outbox repository, RabbitMQ dispatcher |
| **Contracts** | Versioned HTTP request/response DTOs |
| **Api** | Minimal API endpoints, ProblemDetails mapping |
| **Worker** | BackgroundService poll loop, retry logic, dead-lettering |

---

## Dispatch strategies

The dispatch path is controlled by a single configuration key. Both API and Worker must use the same strategy.

### Database (default)

Jobs are dispatched via an `OutboxEntry` written in the same transaction as the job. The Worker polls the `outbox_entries` table with `FOR UPDATE SKIP LOCKED`.

```json
{ "Dispatch": { "Strategy": "Database" } }
```

No additional infrastructure required. Suitable for low-to-medium throughput (see [ADR-015](docs/adrs/015-benchmark-results.md)).

### RabbitMQ

After the job is committed to the database, a message is published to the `import-jobs` queue. The Worker consumes messages via a push-based subscription and acknowledges them after successful processing.

```json
{ "Dispatch": { "Strategy": "RabbitMQ" } }
```

Required additional configuration:

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "<password>",
    "QueueName": "import-jobs"
  }
}
```

RabbitMQ delivers ~3–4× higher throughput than the database strategy at the measured scales. See [ADR-015](docs/adrs/015-benchmark-results.md) for benchmark data and the recommendation on when to switch.

---

## Batch import

Files with more lines than `Batch:ChunkSize` (default: **500**) are automatically processed as batch jobs. Each chunk is committed atomically; a transient failure in one chunk does not discard the work of other chunks.

```json
{ "Batch": { "ChunkSize": 500 } }
```

Batch progress is exposed on the job resource:

```json
{
  "isBatch": true,
  "totalLines": 10000,
  "chunkSize": 500,
  "processedLines": 9500,
  "failedLines": 500
}
```

| Final status | Condition |
|---|---|
| `Succeeded` | All chunks persisted successfully |
| `PartiallySucceeded` | At least one chunk failed; remaining chunks continued |

`processedLines + failedLines` always equals `totalLines`. `DeliveryItems` count always matches `processedLines`.

See [ADR-016](docs/adrs/016-batch-import-strategy.md) for the design rationale.

---

## Key patterns and their ADRs

| Pattern | Where | ADR |
|---|---|---|
| DB-backed outbox over message broker | `OutboxRepository` | [ADR-001](docs/adrs/001-db-queue-over-broker.md) |
| Pessimistic locking with `FOR UPDATE SKIP LOCKED` | `OutboxRepository.ClaimNextAsync` | [ADR-003](docs/adrs/003-pessimistic-locking.md) |
| Raw payload stored separately from job | `ImportPayload` entity | [ADR-004](docs/adrs/004-raw-payload-persistence.md) |
| Transient vs. permanent error classification | `IExceptionClassifier` | [ADR-005](docs/adrs/005-error-classification-transient-vs-permanent.md) |
| Strict status model with enforced transitions | `ImportJobWorkflow` | [ADR-006](docs/adrs/006-status-model-design.md) |
| Stale-lock recovery for orphaned outbox entries | `OutboxRepository.RecoverStaleAsync` | [ADR-012](docs/adrs/012-stale-outbox-lock-recovery.md) |
| Dispatcher abstraction with config-switch | `IJobDispatcher` | [ADR-013](docs/adrs/013-dispatcher-abstraction.md) |
| RabbitMQ integration | `RabbitMqJobDispatcher`, `RabbitMqWorker` | [ADR-014](docs/adrs/014-rabbitmq-integration.md) |
| DB queue vs. RabbitMQ benchmark | BenchmarkDotNet scenarios | [ADR-015](docs/adrs/015-benchmark-results.md) |
| Chunk-based batch processing and partial failures | `LineChunker`, `BatchOptions` | [ADR-016](docs/adrs/016-batch-import-strategy.md) |
| Post-commit RabbitMQ publish | `IAfterSaveCallbackRegistry` | [ADR-017](docs/adrs/017-rabbitmq-post-commit-publish.md) |
| Blazor Server as web frontend | `Ingestor.Web` | [ADR-018](docs/adrs/018-blazor-server-web-ui.md) |
| Persistent Data Protection keys | `PersistKeysToAzureBlobStorage` | [ADR-019](docs/adrs/019-data-protection-azure-blob-storage.md) |

All ADRs are in [`docs/adrs/`](docs/adrs/).

---

## Tech stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 (SDK 10.0.102) |
| API | ASP.NET Core Minimal API |
| Web UI | Blazor Server (ASP.NET Core) |
| ORM | EF Core 10, Npgsql |
| Background jobs | .NET Worker Host (`BackgroundService`) |
| Message broker | RabbitMQ 3 (optional, via `RabbitMQ.Client`) |
| Logging | Serilog (structured, console sink) |
| Tracing | OpenTelemetry (`ActivitySource` per pipeline step) |
| Testing | xUnit, FluentAssertions, Testcontainers, BenchmarkDotNet |
| API docs | OpenAPI 3.1 (`/scalar`) |
| Containers | Docker, Docker Compose |

---

## Quick start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [Docker](https://docs.docker.com/get-docker/)

### 1. Configure environment

Create a `.env` file in the project root (a template is provided):

```bash
# Required
POSTGRES_PASSWORD=<your-db-password>
RABBITMQ_PASSWORD=<your-rabbitmq-password>
CONNECTION_STRING=Host=postgres;Port=5432;Database=ingestor;Username=ingestor;Password=<your-db-password>

# Optional — defaults shown
POSTGRES_DB=ingestor
POSTGRES_USER=ingestor
RABBITMQ_USER=guest
```

### 2. Start all services

```bash
docker compose up
```

This starts PostgreSQL, RabbitMQ, the API, and the Worker. The default `docker-compose.yml` uses the **RabbitMQ** strategy. To use the database strategy instead, set `Dispatch__Strategy=Database` on both `api` and `worker` services.

| Service | URL |
|---|---|
| Web UI | http://localhost:8202 |
| API | http://localhost:8200 |
| Interactive API docs | http://localhost:8200/scalar |
| API health | http://localhost:8200/health |
| Worker health | http://localhost:8201/health |
| RabbitMQ Management UI | http://localhost:15672 |

---

## API endpoints

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/imports` | Upload file and create job |
| `GET` | `/api/imports` | List jobs (filterable by status) |
| `GET` | `/api/imports/{id}` | Job detail with current status and batch progress |
| `GET` | `/api/imports/{id}/history` | Full audit trail |
| `POST` | `/api/imports/{id}/requeue` | Manually retry a failed job |
| `GET` | `/api/metrics/jobs` | Job counts by status |
| `GET` | `/api/metrics/processing` | Average duration and success rate |

---

## Testing

| Project | Scope | Approach |
|---|---|---|
| `Tests.Unit` | Parsers, validators, state machine, retry policy | Pure unit tests, no I/O |
| `Tests.Integration` | Full pipeline, batch import at scale, fault injection, stale-lock recovery | Testcontainers (real PostgreSQL) |
| `Tests.Architecture` | Layer dependency rules | NetArchTest |
| `Tests.Benchmarks` | DB queue vs. RabbitMQ throughput comparison | BenchmarkDotNet |

```bash
dotnet test Ingestor.slnx -c Release
```

---

## Documentation

- [V1 scope](docs/en/scope-v1-en.md)
- [V2 scope](docs/en/scope-v2-en.md)
- [Architecture Decision Records](docs/adrs/)
- [Operational runbook](docs/runbook.md)
- [Changelog](CHANGELOG.md)

---

## License

[MIT](LICENSE)
