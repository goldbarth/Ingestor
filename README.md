# Ingestor — Fleetholm Logistics

<p>
  <img src="https://img.shields.io/github/v/release/goldbarth/Ingestor"/>
  <a href="https://github.com/goldbarth/Ingestor/actions/workflows/ci.yml">
    <img src="https://github.com/goldbarth/Ingestor/actions/workflows/ci.yml/badge.svg" alt="CI" />
  </a>
</p>

A production-grade .NET 10 application for reliable asynchronous import processing. Files are received, validated, processed, and tracked — with structured error handling, automatic retries, and full audit trails.

> Fleetholm Logistics is a fictional company used as the domain context for this project.

---

## Core capabilities

- **Outbox pattern** — jobs are enqueued via a database table; no message broker, no distributed transactions
- **Strict state machine** — all job status transitions are validated at the domain layer; invalid transitions throw
- **Retry with exponential backoff** — transient failures are retried up to a configurable maximum; permanent failures fail immediately
- **Dead-letter & manual requeue** — exhausted jobs are snapshotted and can be requeued via API
- **Stale-lock recovery** — `Processing` entries orphaned by worker crashes are automatically recovered on the next poll cycle
- **Idempotency** — duplicate uploads are detected by SHA-256 content + supplier hash; no double processing
- **Result pattern** — no exceptions cross application layer boundaries; all outcomes are explicit `Result<T>` values
- **Full audit trail** — every status transition is recorded with trigger, timestamp, and context

---

## System architecture

Two independently deployable processes share a single PostgreSQL database.

```
 ┌───────────────────────┐          ┌───────────────────────┐
 │     Ingestor.Api      │          │   Ingestor.Worker     │
 │  POST /api/imports    │          │   BackgroundService   │
 │  GET  /api/imports    │          │   Poll → Process      │
 │  POST /…/requeue      │          │   Retry / Dead-letter │
 └──────────┬────────────┘          └──────────┬────────────┘
            │                                   │
            └─────────────┬─────────────────────┘
                          │
             ┌────────────▼────────────┐
             │       PostgreSQL        │
             │  import_jobs            │
             │  outbox_entries         │
             │  import_payloads        │
             │  delivery_items         │
             │  import_attempts        │
             │  dead_letter_entries    │
             │  audit_events           │
             └─────────────────────────┘
```

---

## Job lifecycle

```
Received ──→ Parsing ──→ Validating ──→ Processing ──→ Succeeded
                │              │               │
                ├──────────────┴───→ ValidationFailed
                │
                └──→ ProcessingFailed ──→ (retry) ──→ Parsing
                                    └──→ (exhausted) ──→ DeadLettered
                                                               │
                                                         (requeue) ──→ Received
```

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
| **Infrastructure** | EF Core, PostgreSQL, outbox repository with pessimistic locking |
| **Contracts** | Versioned HTTP request/response DTOs |
| **Api** | Minimal API endpoints, ProblemDetails mapping |
| **Worker** | BackgroundService poll loop, retry logic, dead-lettering |

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

All 12 ADRs are in [`docs/adrs/`](docs/adrs/).

---

## Tech stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 (SDK 10.0.102) |
| API | ASP.NET Core Minimal API |
| ORM | EF Core 10, Npgsql |
| Background jobs | .NET Worker Host (`BackgroundService`) |
| Logging | Serilog (structured, console sink) |
| Tracing | OpenTelemetry (`ActivitySource` per pipeline step) |
| Testing | xUnit, FluentAssertions, Testcontainers |
| API docs | OpenAPI 3.1 (`/scalar`) |
| Containers | Docker, Docker Compose |

---

## Quick start

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [Docker](https://docs.docker.com/get-docker/)

```bash
docker compose up
```

| Endpoint | URL |
|---|---|
| API | http://localhost:8200 |
| Interactive API docs | http://localhost:8200/scalar |
| API health | http://localhost:8200/health |
| Worker health | http://localhost:8201/health |

---

## API endpoints

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/imports` | Upload file and create job |
| `GET` | `/api/imports` | List jobs (filterable by status) |
| `GET` | `/api/imports/{id}` | Job detail with current status |
| `GET` | `/api/imports/{id}/history` | Full audit trail |
| `POST` | `/api/imports/{id}/requeue` | Manually retry a failed job |
| `GET` | `/api/metrics/jobs` | Job counts by status |
| `GET` | `/api/metrics/processing` | Average duration and success rate |

---

## Testing

| Project | Scope | Approach |
|---|---|---|
| `Tests.Unit` | Parsers, validators, state machine, retry policy | Pure unit tests, no I/O |
| `Tests.Integration` | Full pipeline, fault injection, stale-lock recovery | Testcontainers (real PostgreSQL) |
| `Tests.Architecture` | Layer dependency rules | NetArchTest |

```bash
dotnet test Ingestor.slnx -c Release
```

---

## V2 Roadmap

| Milestone | Focus | Issues |
|---|---|---|
| **M6** | Dispatcher abstraction — `IJobDispatcher` interface + config-switch | #43–#46 |
| **M7** | RabbitMQ integration — broker, consumer, dead-letter exchange | #47–#53 |
| **M8** | Benchmark — DB queue vs. RabbitMQ, BenchmarkDotNet, ADR with real numbers | #54–#56 |
| **M9** | Batch import — chunk processing, progress tracking, partial success | #57–#62 |
| **M10** | V2 documentation — README, runbook, CHANGELOG | #63–#65 |

Full V2 scope: [docs/en/scope-v2-en.md](docs/en/scope-v2-en.md)

---

## Documentation

- [V1 scope](docs/en/scope-v1-en.md)
- [V2 scope](docs/en/scope-v2-en.md)
- [Architecture Decision Records](docs/adrs/)
- [Operational runbook](docs/runbook.md)

---

## License

[MIT](LICENSE)