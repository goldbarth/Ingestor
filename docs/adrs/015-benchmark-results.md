# ADR-015: DB Queue vs. RabbitMQ — Benchmark Results and Recommendation

**Date:** 2026-03-26
**Status:** Accepted

---

## Context

ADR-013 introduced `IJobDispatcher` with two interchangeable implementations: `DatabaseJobDispatcher`
(outbox-based polling) and `RabbitMqJobDispatcher` (push-based messaging). Both implementations
fulfill the same interface contract. M8 benchmarks the two strategies against each other to determine
at what scale — if any — RabbitMQ outperforms the database queue, and to produce a data-driven
recommendation for Ingestor's operating conditions.

---

## Measurement Conditions

All benchmarks were run on a **physical developer machine** (AMD Ryzen 7 5800X, 8 cores / 16 threads,
Windows 11, .NET 10.0.3). This is intentional: a physical machine is the closest available
approximation to a production deployment without a dedicated staging environment. Results reflect
real-world OS scheduling, network stack latency, and disk I/O — factors that are suppressed or
distorted in virtual machines and shared CI runners.

**Consequence:** The absolute numbers in this ADR are machine-specific and will differ on other
hardware. The ordering of strategies and the magnitude of the differences are the meaningful signal,
not the raw millisecond values.

**Benchmark configuration:**
- Tool: BenchmarkDotNet v0.14.0
- Iterations: 5 measured + 1 warmup (`IterationCount=5, WarmupCount=1`)
- Memory tracking: `[MemoryDiagnoser]`
- `Worker:PollingIntervalSeconds = 0` — continuous polling, no sleep between polls
- Each iteration dispatches N jobs through `CreateImportJobHandler` and waits until all reach a terminal status

---

## Raw Results

### Scenario 1 & 2 — Sequential dispatch, 1 worker

| Strategy | Jobs | Mean | StdDev | Throughput | Allocated |
|---|---|---|---|---|---|
| Database | 100 | 1,737.8 ms | 125.2 ms | ~57 jobs/s | 66.7 MB |
| Database | 1,000 | 23,207.9 ms | 1,554.4 ms | ~43 jobs/s | 1,869.1 MB |
| RabbitMQ | 100 | 605.5 ms | 37.7 ms | ~165 jobs/s | 20.2 MB |
| RabbitMQ | 1,000 | 5,747.4 ms | 593.6 ms | ~174 jobs/s | 819.8 MB |

### Scenario 3 — Sequential dispatch, 2 concurrent workers

| Strategy | Jobs | Mean | StdDev | Throughput | Allocated |
|---|---|---|---|---|---|
| Database | 100 | 1,898.9 ms | 322.3 ms | ~53 jobs/s | 93.0 MB |
| RabbitMQ | 100 | 646.1 ms | 47.0 ms | ~155 jobs/s | 25.4 MB |

*Note: The Database/Concurrent result had one outlier removed by BenchmarkDotNet (N=4 effective).
The high StdDev (17%) reflects genuine lock contention variability under concurrent polling.*

---

## Interpretation

### RabbitMQ is consistently faster at all measured scales

RabbitMQ outperforms the database queue by a factor of **~3× at 100 jobs** and **~4× at 1,000 jobs**
in sequential scenarios. The gap widens with volume because the two strategies have fundamentally
different scaling characteristics:

- **Database**: throughput degrades as volume increases (57 → 43 jobs/s from 100 to 1,000 jobs).
  Each additional job means another poll cycle, more rows to skip, and higher EF Core allocation pressure.
- **RabbitMQ**: throughput is stable or slightly improves with volume (165 → 174 jobs/s).
  The broker delivers messages immediately upon publish; processing time per job does not accumulate
  polling overhead.

### A second worker does not accelerate either strategy at this scale

Adding a second concurrent worker yields **no measurable throughput improvement** for either strategy
at 100 jobs:

- Database concurrent (2 workers): 53 jobs/s vs. 57 jobs/s sequential — marginally *slower*.
  Two workers polling with `FOR UPDATE SKIP LOCKED` introduces competing lock attempts; at low job
  volumes, the coordination cost outweighs the parallelism benefit.
- RabbitMQ concurrent (2 workers): 155 jobs/s vs. 165 jobs/s sequential — statistically equivalent.
  The benchmark dispatches jobs sequentially through a single handler, so the dispatch sequence is
  the bottleneck regardless of worker count. A second worker adds value only when jobs arrive
  concurrently from multiple sources or when individual jobs take significant time to process.

### Memory

RabbitMQ allocates **~3× less managed memory** than the database strategy at 100 jobs
(20 MB vs. 67 MB) and **~2.3× less** at 1,000 jobs (820 MB vs. 1,869 MB). The database strategy
allocates for `OutboxEntry` construction, EF change tracking per poll cycle, and the accumulated
entity graph across the run.

---

## Critical Caveat: `PollingIntervalSeconds = 0`

The benchmarks set `Worker:PollingIntervalSeconds = 0`, which disables the inter-poll sleep and runs
the worker at theoretical maximum throughput. **Production deployments use a non-zero interval**
(default: 5 seconds). With a 5-second polling interval, the database strategy's effective throughput
drops significantly: a job submitted immediately after a poll cycle waits up to 5 seconds before
being picked up. RabbitMQ is unaffected by this — it delivers messages as soon as they are published.

The benchmark numbers for the database strategy therefore represent an **upper bound**, not a
realistic production figure. The actual production throughput gap between the two strategies
is larger than these numbers suggest.

---

## Recommendation for Ingestor's Current Scale

**Retain the config-switch; use Database as the default.**

At Ingestor's current operating conditions — single-supplier file imports, no concurrent batch
bursts, single worker deployment — the database strategy is operationally sufficient. The throughput
ceiling of ~43–57 jobs/s (at `PollingIntervalSeconds=0`) far exceeds any realistic submission rate
for a B2B delivery advice import system.

The database strategy requires no additional infrastructure: no broker to operate, no connection
recovery to manage, no dead-letter queue to inspect separately from the job table. For a deployment
where PostgreSQL is already present, the operational cost is zero.

**Switch to RabbitMQ when any of the following conditions apply:**

1. **Job submission rate exceeds ~5 jobs/minute in production** — at a realistic
   `PollingIntervalSeconds=5`, the database strategy introduces up to 5 seconds of queueing latency
   per job. RabbitMQ eliminates this latency entirely.
2. **Worker horizontal scaling is required** — beyond 2 concurrent workers, the `FOR UPDATE SKIP LOCKED`
   contention cost of the database strategy will grow. RabbitMQ's queue model distributes work
   without coordination overhead.
3. **End-to-end latency is a user-visible SLA** — if a submitting client expects near-real-time
   processing confirmation, polling latency makes the database strategy unsuitable regardless of
   throughput.
4. **Job volume exceeds ~500 jobs per run** — at 1,000 jobs, the database strategy already allocates
   1.9 GB of managed memory per benchmark iteration. At sustained high volume, GC pressure becomes
   a concern.

---

## Consequences

### What this ADR confirms

- The dispatcher abstraction (ADR-013) is validated: both backends are measurably different in
  performance characteristics but functionally equivalent at the interface boundary.
- The database queue is not a "temporary workaround" — it is a legitimate choice for low-to-medium
  volume deployments and removes an entire infrastructure dependency.
- The RabbitMQ advantage is real and consistent, not marginal. Any team that has RabbitMQ already
  provisioned should prefer it.

### Limitations of this benchmark

- **N=5 with 1 warmup** provides orientating data, not statistically rigorous confidence intervals.
  The 99.9% CI for several scenarios is wide (25–40% of mean). The ordering is reliable; the exact
  values are not.
- **Single-line CSV payload** — each job processes one delivery item. Real workloads with larger
  files will shift the balance further toward RabbitMQ (processing time dominates, making queueing
  latency more visible).
- **Loopback networking** — broker and worker run on the same machine. In a multi-host deployment,
  RabbitMQ network latency would add overhead not captured here.
- **No retry scenarios** — all jobs succeed on the first attempt. The database strategy's retry
  scheduling (exponential backoff via `OutboxEntry.ScheduledFor`) is not exercised.
