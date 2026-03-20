# Scope V2 — Ingestor: Message Broker & Batch Processing

## Core Goal

V1 proved that the system can operate reliably with a database-backed queue. V2 introduces RabbitMQ as an alternative dispatch strategy — not as a replacement, but as a configurable option. On top of that, V2 adds batch import support for large files.

The real value of V2 is not in claiming that RabbitMQ is "better," but in backing the decision between both strategies with real benchmark data.

---

## What V2 Adds on Top of V1

| Area | V1 | V2 |
|---|---|---|
| Job dispatch | DB queue (Outbox + polling) | DB queue **or** RabbitMQ (config switch) |
| Abstraction | Directly coupled to `OutboxEntry` | `IJobDispatcher` interface |
| Performance evidence | — | BenchmarkDotNet: DB vs. RabbitMQ |
| Batch processing | One file = one job | One file with 10,000+ lines = batch job |
| Infrastructure | PostgreSQL + API + Worker | + RabbitMQ (optional via Docker Compose) |

---

## Required Capabilities

### 1. Dispatcher Abstraction

- `IJobDispatcher` interface with two operations: `Dispatch(job)` and `Acknowledge(job)`
- `DatabaseJobDispatcher` as an extraction of the existing V1 outbox logic
- `RabbitMqJobDispatcher` as a new implementation
- Registration through DI, controlled by configuration (`appsettings.json`)
- No feature-flag system required; a simple config value is enough: `Dispatch:Strategy = "Database"` or `"RabbitMQ"`
- Both implementations must remain runnable at all times

**Why a config switch instead of replacing the old approach:**
- In production, you want to validate the broker before fully switching over
- The benchmark comparison requires both implementations
- It demonstrates clean interface design and dependency inversion

### 2. RabbitMQ Integration

- Extend Docker Compose with RabbitMQ (including the Management UI)
- `RabbitMqJobDispatcher` publishes job events to a queue
- The worker consumes from the queue instead of polling
- Acknowledge messages after successful processing
- Configure a dead-letter exchange for failed messages
- Handle reconnects after connection loss

**Deliberate boundaries:**
- No complex exchange or routing setup — one queue and one consumer are enough
- No custom retry mechanism via RabbitMQ DLX — the existing V1 retry logic remains in place
- RabbitMQ is the transport layer, not the failure-control mechanism

### 3. Throughput Benchmark

- BenchmarkDotNet project inside the test folder
- Scenarios:
  - 100 jobs sequentially
  - 1,000 jobs sequentially
  - 100 jobs in parallel (multiple workers)
- Metrics to capture: throughput (jobs per second), latency (time from dispatch to processing start), resource usage (CPU, memory)
- Results documented in an ADR, including interpretation — not just raw numbers

### 4. Batch Import

- New import variant: one file containing many rows (10,000+)
- The batch is processed in chunks (for example, 500 rows per chunk)
- Chunk size is configurable
- Batch progress is visible on the job: `ProcessedLines` / `TotalLines`
- Partial failure must be supported: some chunks may succeed while others fail
- New batch-only status: `PartiallySucceeded`
- Existing single-file imports remain unchanged

**Deliberate boundaries:**
- No streaming or chunked upload — the file is uploaded completely first, then split into chunks
- No parallel chunk processing in V2 — sequential per job; parallel chunk execution is a V3 topic

---

## Status Model Extension

New status for batch jobs only:

```text
Processing → PartiallySucceeded (some chunks failed)
```

`PartiallySucceeded` is a terminal state. The job has produced results, but not all rows were processed successfully. Failed rows remain traceable through attempt and validation errors.

All existing V1 states and transitions stay unchanged.

---

## Data Model Extensions

### `ImportJob` — New Fields

| Field | Type | Description |
|---|---|---|
| `TotalLines` | `int?` | Total number of rows (batch only) |
| `ProcessedLines` | `int?` | Number of rows processed so far |
| `FailedLines` | `int?` | Number of failed rows |
| `IsBatch` | `bool` | Marks the job as a batch import |
| `ChunkSize` | `int?` | Configured chunk size |

> Nullable because existing single-file imports do not use these fields.

---

## Tech Stack Additions on Top of V1

| Component | Purpose |
|---|---|
| RabbitMQ 3.x | Message broker (optional) |
| RabbitMQ.Client | .NET client library |
| BenchmarkDotNet | Throughput comparison |
| Docker Compose | Extended with RabbitMQ + Management UI |

---

## Explicit Non-Goals

- **No complex exchange routing** — a single queue is enough
- **No RabbitMQ-based retry strategy** — V1 retry logic stays in place
- **No parallel chunk processing** — sequential per job
- **No streaming upload** — the file is uploaded in full
- **No migration of existing jobs** — only new jobs use the configured dispatcher
- **No Kafka, no Azure Service Bus** — RabbitMQ is sufficient for the learning goal

---

## Implementation Order

### Phase 1: Dispatcher Abstraction (Week 1)

- Define the `IJobDispatcher` interface
- Extract `DatabaseJobDispatcher` from the existing outbox logic
- Add DI registration with a config switch
- Existing tests continue to pass (regression safety)
- ADR: Dispatcher abstraction and config-switch strategy

### Phase 2: RabbitMQ Integration (Week 2–3)

- Add RabbitMQ to Docker Compose
- Implement `RabbitMqJobDispatcher`
- Extend the worker with a queue consumer
- Configure a dead-letter exchange
- Add connection recovery for disconnect scenarios
- Integration test: dispatch and process a job through RabbitMQ

### Phase 3: Benchmark (Week 3–4)

- Set up the BenchmarkDotNet project
- Implement benchmark scenarios
- Run measurements
- ADR: DB queue vs. RabbitMQ — results and recommendation

### Phase 4: Batch Import (Week 4–6)

- Add chunking logic to the processing step
- Add `TotalLines` / `ProcessedLines` / `FailedLines` to `ImportJob`
- Add the `PartiallySucceeded` status
- Progress endpoint: `GET /api/imports/{id}` shows batch progress
- Unit tests: chunk splitting and partial failure cases
- Integration test: process a file with 10,000 rows

### Phase 5: Documentation (Week 6)

- Complete the ADRs
- Update the README (V2 features, configuration)
- Extend the runbook with RabbitMQ troubleshooting
- Update the CHANGELOG

---

## Documentation (Planned ADRs)

| # | Topic |
|---|---|
| 007 | Dispatcher abstraction and config-switch strategy |
| 008 | RabbitMQ integration: scope and boundaries |
| 009 | DB queue vs. RabbitMQ — benchmark results |
| 010 | Batch import: chunking strategy and partial failures |

---

## What the Portfolio Tells Afterwards

V1 says: *"I can build a reliable backend system."*

V2 says: *"I can evaluate technical alternatives, compare them with real numbers, and document the decision."*

That is exactly the difference between *"I know RabbitMQ"* and *"I know when RabbitMQ is worth it — and when it is not."*
