# Scope V1 — Ingestor: Delivery Data Import System

## Core Objective

A reliable import system that receives delivery data from external partners, validates and processes it, and handles failures in a controlled way. Every step is traceable, errors are classified, retries are managed automatically, and no data is lost.

## Business Context

A fictional home and furniture logistics operator ("Fleetholm Logistics") coordinates deliveries from multiple furniture and interior suppliers. Suppliers send daily delivery notice files containing line items such as article number, product name, quantity, expected delivery date, and reference number.

The challenge is that suppliers deliver data in different formats (CSV, JSON) and with varying quality levels. Some files are clean, while others contain missing fields, duplicates, or invalid values. Furniture logistics adds further complexity through long delivery lead times, partial deliveries, and frequent schedule changes.

> The business context is intentionally kept simple. The value of the project lies not in domain complexity, but in the technical reliability of the processing pipeline.

---

## Required Capabilities

### Intake

- Create an import job through an API endpoint (`POST` with file upload)
- Accept CSV and JSON files
- Persist raw data separately (the payload is stored unchanged)
- Generate an idempotency key for each upload (hash of file content + supplier ID)
- Detect duplicates before processing starts
- Register the job immediately with status `Received`

### Processing

- A worker processes jobs asynchronously
- Parsing stage: CSV → internal model / JSON → internal model
- Validation stage: required fields, allowed ranges, reference plausibility
- Processing stage: write mapped data into the target table (`DeliveryItem`)
- Each stage updates the job status
- Persist the result (number of processed and failed rows)

### Error Handling

- Error categories: `Transient` (retryable) vs. `Permanent` (dead letter)
- Retry policy: maximum of 3 attempts with exponential backoff
- Every attempt is recorded as an `ImportAttempt`
- Once retries are exhausted → status `DeadLettered`
- Manual requeue through an API endpoint
- Idempotent job processing (the same job must not run twice)

### Transparency (Audit & Tracking)

- Every status change is persisted as an `AuditEvent`
- Full job history can be reconstructed
- Attempt history includes duration, outcome, and error category
- Root causes are structured, not just free text
- A correlation ID is carried through logs and traces for each job
- Requeue actions are visible in the audit trail

### Operations

- Health checks (database connectivity, worker heartbeat)
- Structured logging with Serilog + correlation ID
- OpenTelemetry traces across the full pipeline
- Metrics endpoints: jobs by status, average processing time
- Docker Compose setup (API + Worker + PostgreSQL)
- CI pipeline (GitHub Actions)
- Integration tests with Testcontainers

---

## Architecture

### Topology — 2 Processes

| Process         | Role                          |
|-----------------|-------------------------------|
| **API Host**    | Intake & job registration     |
| **Worker Host** | Asynchronous processing       |

**Why 2 processes:**

- Clear separation between intake and processing
- Realistic runtime split (the API can scale or restart independently from the worker)
- Distinct responsibilities and lifecycle
- Operational thinking is visible to reviewers

> Stronger than a single-process setup, while still remaining easy to reason about. No microservice overhead.

### Modules

#### 1. Intake

- Accept and register import jobs
- Persist the payload (raw data)
- Check idempotency
- Create an `OutboxEntry` for the worker

#### 2. Processing

- Load the job from the outbox
- Parse (CSV/JSON → internal model)
- Run business validation
- Map data into the target table (`DeliveryItem`)
- Set the resulting status

#### 3. Retry & Failure Handling

- Classify errors (`Transient` / `Permanent`)
- Schedule retries with backoff
- Move to dead letter after maximum attempts
- Support manual requeue

#### 4. Audit / Tracking

- Job status history
- Attempt history
- State transitions as events
- Structured error details

#### 5. Observability / Operations

- Structured logging (Serilog)
- Distributed tracing (OpenTelemetry)
- Metrics
- Health checks
- Operational diagnostics

---

## Status Model

The status model is explicit and strict. Every transition is defined, and there are no implicit states.

### States

| Status              | Meaning                                              |
|---------------------|------------------------------------------------------|
| `Received`          | Job created, payload persisted                       |
| `Parsing`           | Worker has claimed the job, parsing is in progress   |
| `Validating`        | Parsing succeeded, business validation is running    |
| `Processing`        | Validation passed, data is being written             |
| `Succeeded`         | Processing completed                                 |
| `ValidationFailed`  | Permanent failure — data is business-invalid         |
| `ProcessingFailed`  | Transient failure — may be retried                   |
| `DeadLettered`      | All retries exhausted or manually escalated          |

### Allowed Transitions

```text
Received → Parsing
Parsing → Validating
Parsing → ProcessingFailed (parser failure, transient)
Parsing → ValidationFailed (file fundamentally unreadable)
Validating → Processing
Validating → ValidationFailed
Processing → Succeeded
Processing → ProcessingFailed
ProcessingFailed → Parsing (retry)
ProcessingFailed → DeadLettered (max attempts reached)
DeadLettered → Received (manual requeue)
```

> **Important:** `ValidationFailed` is a terminal state. Business-invalid data is not retried automatically. It can only be requeued manually after the source data has been corrected.

---

## Messaging Strategy (V1)

### Decision: Database-Backed Queue

Jobs are stored in an outbox table, and the worker polls for them.

| Advantages                        | Trade-offs                    |
|-----------------------------------|-------------------------------|
| Simple, less infrastructure       | No real messaging system      |
| Transactional consistency with DB | Polling overhead              |
| Strong focus on state modeling    | Less scalable under high load |

**Technical approach:**

- `OutboxEntry` table with status `Pending` / `Processing` / `Done`
- Worker polls at a configurable interval
- `SELECT ... FOR UPDATE SKIP LOCKED` for concurrency safety
- No job is processed twice

> **V2 perspective:** RabbitMQ as an alternative, with an `IJobDispatcher` abstraction and a documented throughput comparison (BenchmarkDotNet). See `scope-v2.md`.

---

## Data Model

### `ImportJob`

| Field              | Type       | Description                                  |
|--------------------|------------|----------------------------------------------|
| `Id`               | UUID       | Primary key                                  |
| `SupplierCode`     | string     | Supplier identifier                          |
| `ImportType`       | enum       | `CsvDeliveryAdvice`, `JsonDeliveryAdvice`    |
| `Status`           | enum       | Current job status (see status model)        |
| `IdempotencyKey`   | string     | Hash of file content + `SupplierCode`        |
| `PayloadReference` | string     | Reference to persisted raw payload           |
| `ReceivedAt`       | timestamp  | Time received                                |
| `StartedAt`        | timestamp? | Processing start time                        |
| `CompletedAt`      | timestamp? | Completion time                              |
| `CurrentAttempt`   | int        | Current attempt counter                      |
| `MaxAttempts`      | int        | Maximum attempts (default: 3)                |
| `LastErrorCode`    | string?    | Last error code                              |
| `LastErrorMessage` | string?    | Last error message                           |

### `ImportAttempt`

| Field            | Type       | Description                       |
|------------------|------------|-----------------------------------|
| `Id`             | UUID       | Primary key                       |
| `JobId`          | UUID       | FK → `ImportJob`                  |
| `AttemptNumber`  | int        | Attempt number                    |
| `StartedAt`      | timestamp  | Start time                        |
| `FinishedAt`     | timestamp? | End time                          |
| `Outcome`        | enum       | `Succeeded`, `Failed`             |
| `ErrorCategory`  | enum?      | `Transient`, `Permanent`          |
| `ErrorCode`      | string?    | Structured error code             |
| `ErrorMessage`   | string?    | Error description                 |
| `DurationMs`     | long       | Duration in milliseconds          |

### `ImportPayload`

| Field         | Type         | Description                        |
|---------------|--------------|------------------------------------|
| `Id`          | UUID         | Primary key                        |
| `JobId`       | UUID         | FK → `ImportJob`                   |
| `ContentType` | string       | `text/csv`, `application/json`     |
| `RawData`     | text/bytes   | Unmodified raw payload             |
| `SizeBytes`   | long         | File size                          |
| `ReceivedAt`  | timestamp    | Time received                      |

### `DeliveryItem` (Target Table)

| Field            | Type       | Description                           |
|------------------|------------|---------------------------------------|
| `Id`             | UUID       | Primary key                           |
| `JobId`          | UUID       | FK → `ImportJob` (origin)             |
| `ArticleNumber`  | string     | Article number                        |
| `ProductName`    | string     | Product name                          |
| `Quantity`       | int        | Quantity                              |
| `ExpectedDate`   | date       | Expected delivery date                |
| `SupplierRef`    | string     | Supplier reference number             |
| `ProcessedAt`    | timestamp  | Processing timestamp                  |

### `DeadLetterEntry`

| Field         | Type       | Description                           |
|---------------|------------|---------------------------------------|
| `Id`          | UUID       | Primary key                           |
| `JobId`       | UUID       | FK → `ImportJob`                      |
| `Reason`      | string     | Reason for dead-lettering             |
| `FinalizedAt` | timestamp  | Time of finalization                  |
| `Snapshot`    | jsonb      | Job state snapshot at that moment     |

### `AuditEvent`

| Field          | Type      | Description                                      |
|----------------|-----------|--------------------------------------------------|
| `Id`           | UUID      | Primary key                                      |
| `JobId`        | UUID      | FK → `ImportJob`                                 |
| `EventType`    | string    | e.g. `StatusChanged`, `Requeued`, `DeadLettered` |
| `OldStatus`    | enum?     | Previous status                                  |
| `NewStatus`    | enum?     | New status                                       |
| `Timestamp`    | timestamp | Time of the event                                |
| `TriggeredBy`  | string    | `System`, `Worker`, `API`                        |
| `MetadataJson` | jsonb     | Additional metadata                              |

### `OutboxEntry`

| Field         | Type       | Description                     |
|---------------|------------|---------------------------------|
| `Id`          | UUID       | Primary key                     |
| `JobId`       | UUID       | FK → `ImportJob`                |
| `Status`      | enum       | `Pending`, `Processing`, `Done` |
| `CreatedAt`   | timestamp  | Creation time                   |
| `LockedAt`    | timestamp? | Time when claimed               |
| `ProcessedAt` | timestamp? | Completion time                 |

---

## Technical Stack

### Core

- .NET 8 (or .NET 10 at release time)
- ASP.NET Core Minimal API
- Worker Service (`BackgroundService`)
- PostgreSQL
- EF Core

### Runtime / Infrastructure

- Docker Compose (API Host + Worker Host + PostgreSQL)
- GitHub Actions (build, test, lint)
- OpenTelemetry (traces + metrics)
- Serilog with structured JSON output

### Tests

- xUnit
- FluentAssertions
- Testcontainers (PostgreSQL)

### API Documentation

- Scalar (OpenAPI)
- `ProblemDetails` for all error scenarios

---

## Explicit Non-Goals

- **No frontend** — API only
- **No Kubernetes** — Docker Compose is sufficient
- **No cloud deployment** — must run locally
- **No full auth system** — API key or simple bearer token is enough
- **No data lake / analytics**
- **No real-time streaming**
- **No generic framework** — a concrete system for a concrete use case
- **No RabbitMQ in V1** — see messaging strategy
- **No multi-region / multi-tenant support**
- **No actual email sending** — notifications are out of scope

---

## Build Sequence

### Week 1–2: Foundation

- Solution structure, projects, Docker Compose
- DB schema: `ImportJob`, `ImportPayload`, `OutboxEntry`
- `POST` endpoint: upload file → create job → write to outbox
- `GET` endpoint: query job status
- First ADR: "Why a DB queue instead of a message broker"

### Week 3–4: Processing Pipeline

- Parsers for CSV and JSON
- Validator with structured errors
- Populate the `DeliveryItem` table
- End-to-end status transitions
- Unit tests for parser + validator

### Week 5–6: Reliability

- Background worker with DB polling (`SELECT ... FOR UPDATE SKIP LOCKED`)
- Retry logic with exponential backoff
- Dead-letter mechanism
- Manual requeue
- Idempotency checks
- ADR: idempotency strategy

### Week 7–8: Observability & Audit

- OpenTelemetry traces for the pipeline
- Serilog with correlation ID
- Health checks
- Metrics endpoints
- `AuditEvent` table + write logic
- Integration tests with Testcontainers

### Week 9–10: Hardening & Documentation

- Failure tests: what happens if the DB goes down during processing?
- Edge cases: empty file, huge file, invalid encoding
- Finalize the CI/CD pipeline
- Complete the ADRs
- Runbook: "What to do with dead-lettered jobs?"
- README with architecture overview

---

## Documentation (Planned ADRs)

| #   | Topic                                          |
|-----|------------------------------------------------|
| 001 | DB queue instead of a message broker in V1     |
| 002 | Idempotency strategy                           |
| 003 | Pessimistic locking (`SKIP LOCKED`)            |
| 004 | Persist raw data separately                    |
| 005 | Error classification: transient vs. permanent  |
| 006 | Status model design                            |

---

## V2 Outlook

See `scope-v2.md` (to be created separately):

- RabbitMQ as an alternative to the DB queue
- `IJobDispatcher` abstraction (DB + RabbitMQ)
- BenchmarkDotNet: throughput comparison between DB polling and RabbitMQ
- Batch import (process 10,000+ rows efficiently)
- ADR: when is a broker worth it?
