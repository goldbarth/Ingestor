# ADR-001: Database-Backed Queue over Message Broker

**Date:** 2026-03-15
**Status:** Accepted

---

## Context

The Ingestor system requires a mechanism to hand off newly received import jobs from the API to the background worker for processing. This coordination layer needs to be:

- **Reliable** — no job must be lost between receipt and processing
- **Transactional** — the job record and the processing signal must be created atomically
- **Observable** — job status must be queryable at any point in time

The two primary approaches considered were:

1. **Message broker** (e.g. RabbitMQ, Azure Service Bus) — a dedicated messaging infrastructure that decouples producers from consumers
2. **Database-backed queue** (Outbox pattern) — a table in the existing PostgreSQL database acts as the queue

---

## Decision

We use a **database-backed queue via the Outbox pattern**.

On job creation, an `OutboxEntry` row is written in the same transaction as the `ImportJob` and `ImportPayload` rows. The background worker polls the `outbox_entries` table using `SELECT ... FOR UPDATE SKIP LOCKED` to safely claim entries for processing.

---

## Consequences

### Benefits

- **Transactional consistency** — the job and its processing signal are created atomically. There is no window where a job exists without a corresponding outbox entry, or vice versa.
- **No additional infrastructure** — PostgreSQL is already a hard dependency. Introducing a message broker would add operational complexity (deployment, monitoring, credentials, retry configuration) with no benefit at V1 scale.
- **Simplicity** — the entire system state is in one database. Debugging, auditing, and observability require no cross-system correlation.
- **Testability** — integration tests use a single Testcontainers PostgreSQL instance with no additional broker setup.

### Trade-offs

- **Polling overhead** — the worker polls at a fixed interval rather than reacting to push events. This introduces latency equal to the polling interval and generates continuous DB reads even when there is nothing to process.
- **Throughput ceiling** — at high ingestion volumes, the polling-based approach and row-level locking become a bottleneck. A message broker would handle fan-out and parallel consumption more efficiently.
- **Not a general-purpose queue** — features like message routing, dead-letter exchange configuration, or consumer groups require custom implementation rather than broker primitives.

### When to revisit

Switch to a message broker (e.g. RabbitMQ with MassTransit) if any of the following conditions are met:

- Sustained ingestion rate exceeds ~100 jobs/minute and polling latency becomes measurable
- Multiple independent consumer types need to react to the same job event
- Operational tooling around the broker (dashboards, replay, DLQ management) is already available in the infrastructure
