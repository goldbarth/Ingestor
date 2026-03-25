# ADR-014: RabbitMQ Integration Scope and Boundaries

**Date:** 2026-03-25
**Status:** Accepted

---

## Context

M7 introduces RabbitMQ as an optional dispatch backend, selectable via `Dispatch:Strategy=RabbitMQ` (ADR-013). The integration required concrete decisions in three areas: queue topology, consumer model, and error/retry handling.

For each area, multiple approaches are viable. The choices below are scoped to Ingestor's current requirements: single-supplier sequential imports, a single Worker process, and throughput measured in tens to hundreds of jobs per day. Decisions that would be appropriate at higher scale are explicitly deferred.

---

## Decision

### Single queue, single consumer

One durable queue (`import-jobs`) handles all job dispatches. The Worker registers a single consumer with `prefetchCount=1`.

Alternatives considered:

- **Per-supplier or per-type queues** via a topic exchange (e.g., `routing_key=supplier.csv`). Rejected: the routing granularity adds topology complexity without benefit. At current scale, a single queue with sequential processing is simpler to operate and sufficient for throughput.
- **Competing consumers / parallel workers** using `prefetchCount > 1`. Rejected: each job processes a file against the database. Parallelism at the consumer level would race on `ImportJob` state transitions and `DeliveryItem` inserts. The correct lever for throughput is horizontal scaling of the Worker process, not consumer-level concurrency within one process.

The `prefetchCount=1` setting ensures the Worker holds at most one unacknowledged message at a time. Combined with `autoAck=false`, this gives precise control over job lifecycle: a message is only removed from the broker after the pipeline completes successfully.

### Dead-Letter Exchange for rejected messages, without automatic re-routing

A fanout Dead-Letter Exchange (`import-jobs.dlx`) is declared alongside a dead-letter queue (`import-jobs.dead-letters`). The main queue carries the `x-dead-letter-exchange` argument pointing to this exchange. Any message rejected with `requeue=false` is automatically routed to the dead-letter queue and remains visible in the Management UI.

No automatic re-routing from the DLX back to the main queue is configured.

Alternatives considered:

- **RabbitMQ-native retry via TTL chains**: messages rejected from the main queue route to a waiting queue with a TTL, expire back to the main queue via DLX, and are retried. Common pattern, but requires one queue per distinct delay level to implement exponential backoff, and relies on RabbitMQ header counters to track attempt numbers — duplicating logic that already lives in the Worker.
- **Single DLX with manual re-queue by operators**: the chosen approach. The V1 retry mechanism — exponential backoff, transient/permanent classification, `MaxAttempts` per job — is already implemented and tested in the Worker. Replicating this in broker topology would split the retry semantics across two systems, making both harder to reason about.

The DLX serves a single purpose: parking permanently rejected messages for inspection. Retry decisions are made exclusively by the Worker.

### Startup retry on connection failure; library-managed runtime recovery

On startup, `RabbitMqWorker.ExecuteAsync` retries the initial connection in a loop, catching `BrokerUnreachableException` and waiting `InitialConnectionRetryIntervalSeconds` (default: 5s) between attempts. The loop exits when the connection succeeds or the host cancellation token is triggered.

Mid-operation connection drops are delegated to the `AutorecoveringConnection` provided by `RabbitMQ.Client`. The library automatically reconnects, redeclares topology, and re-registers the consumer. `RabbitMqConnectionManager` hooks into `ConnectionShutdownAsync` and `RecoverySucceededAsync` to log connection lifecycle events at `Warning` and `Information` level respectively.

This separation keeps the startup retry policy in application code (where it can be adjusted without touching the library) while leveraging the library's battle-tested reconnect logic for in-flight outages.

---

## Consequences

### Benefits

- **Simple topology, easy to operate** — two queues and one exchange are visible in the Management UI. No routing keys or binding rules to debug.
- **Retry logic in one place** — the Worker owns all retry decisions: delay calculation, attempt counting, transient vs. permanent classification. No split between broker configuration and application code.
- **Dead-lettered messages are auditable** — rejected messages accumulate in `import-jobs.dead-letters` and can be inspected or manually re-queued without losing the original payload.
- **Resilient startup** — the Worker does not crash on broker unavailability at startup; it retries until the broker is reachable or the host is stopped.

### Trade-offs

- **No broker-side parallelism** — `prefetchCount=1` and a single consumer mean throughput is bounded by single-job processing time. Scaling requires deploying additional Worker instances, not tuning the consumer.
- **No automatic DLX retry** — permanently rejected messages require manual operator action (inspect → fix root cause → re-queue via API). This is intentional: automatic re-routing from the DLX would obscure failures that require human intervention.
- **`guest` user restricted to loopback** — integration tests must configure a non-`guest` RabbitMQ user because the `guest` account is restricted to `localhost` connections by default. The `RabbitMqBuilder` in Testcontainers sets `RABBITMQ_DEFAULT_USER/PASS` to `test/test` for the test container.

### What was explicitly excluded

| Capability | Reason for exclusion |
|---|---|
| Topic exchange with routing keys | No routing requirement at current scale |
| Per-message TTL | No SLA on job processing time |
| Message priority levels | All jobs are treated equally |
| Competing consumers within one process | Races on DB state; horizontal scaling is the correct lever |
| Automatic retry via TTL/DLX chains | Duplicates existing Worker retry logic |
| RabbitMQ Streams | Overkill for current throughput; no replay requirement |

### When to revisit

- If jobs need to be routed differently based on supplier or import type (topic exchange).
- If Worker horizontal scaling is introduced and per-consumer prefetch needs tuning.
- If SLA requirements mandate broker-side TTL or priority queues.