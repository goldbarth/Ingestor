# ADR-016: Batch Import Strategy — Chunk-Based Sequential Processing and Partial Failure Semantics

**Date:** 2026-03-28
**Status:** Accepted

---

## Context

The import pipeline processes delivery advice files that may contain tens of thousands of lines.
Three processing strategies are available at the extremes:

1. **Whole-file transaction** — parse and persist all lines in a single `SaveChangesAsync` call.
2. **Line-by-line** — one `SaveChangesAsync` call per line.
3. **Chunk-based** — split lines into fixed-size batches; one `SaveChangesAsync` call per chunk.

Whole-file and line-by-line impose opposite failure and throughput characteristics:

| Property | Whole-file | Line-by-line | Chunk-based |
|---|---|---|---|
| DB round-trips | 1 | N | N / K |
| Blast radius on failure | All N lines lost | 1 line | ≤ K lines |
| Memory pressure | Peak at N lines | Minimal | Bounded at K lines |
| Partial success possible | No | Yes (but impractical) | Yes |

**Whole-file** is unacceptable for large inputs: a transient database timeout at line 9,500 of a
10,000-line file discards all prior work and forces a full retry from scratch.

**Line-by-line** is unacceptable for throughput: 10,000 individual `SaveChangesAsync` calls saturate
the connection pool, inflate EF Core change-tracking overhead, and make the pipeline 1–2 orders of
magnitude slower than necessary.

Neither strategy supports a meaningful `PartiallySucceeded` status: whole-file is all-or-nothing,
and line-by-line would require tracking every individual line failure at the job level.

A secondary question is whether chunks should be processed **sequentially or in parallel**.
Parallel processing appears attractive but is incompatible with the current infrastructure:
`IngestorDbContext` is registered with a **scoped lifetime** and is therefore not thread-safe.
Sharing a single context across parallel tasks would cause race conditions in EF Core's
change tracker. Per-chunk scoping would require restructuring the entire pipeline's dependency
graph and would complicate `ProcessedLines` / `FailedLines` accounting, which currently
happens on the same tracked job entity.

---

## Decision

The pipeline uses **chunk-based sequential processing** controlled by `BatchOptions.ChunkSize`
(default: 500).

### Chunking

`LineChunker.Split` wraps `Enumerable.Chunk` to partition the parsed line list into fixed-size
segments:

```csharp
chunks = LineChunker.Split(parseResult.Lines, batchOptions.Value.ChunkSize);
// 10 000 lines → 20 chunks of 500
```

A job is treated as a **batch** when `chunks.Count > 1`. Batch mode enables per-chunk progress
tracking (`InitializeBatch`, `RecordChunkProcessed`, `RecordChunkFailed`) and partial failure
handling. Single-chunk jobs use a simplified path with one save for all items.

### Sequential execution

Chunks are processed in a `for` loop on the same `ImportPipelineHandler` call stack. No
`Task.WhenAll`, no `Parallel.ForEachAsync`. The single scoped `DbContext` is used throughout,
keeping EF Core's change tracker consistent and avoiding connection pool pressure.

### Per-chunk atomicity

Within each chunk iteration, delivery items are added to the EF change tracker with
`AddRangeAsync` and the job's progress counter is updated with `RecordChunkProcessed` before
a single `SaveChangesAsync` commits both atomically:

```csharp
await deliveryItemRepository.AddRangeAsync(chunkItems, ct);
totalCount += chunkItems.Count;
job.RecordChunkProcessed(chunkItems.Count);
await unitOfWork.SaveChangesAsync(ct); // items + progress update in one transaction
```

### Partial failure semantics

When `SaveChangesAsync` throws (transient database error, constraint violation, etc.) the chunk
exception is caught and the pipeline continues with the remaining chunks:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException && isBatch)
{
    totalCount -= chunk.Count;
    job.RollbackChunkProcessed(chunk.Count);
    job.RecordChunkFailed(chunk.Count);
    await unitOfWork.SaveChangesAsync(ct); // persist failure counters only
}
```

`EfUnitOfWork.SaveChangesAsync` detaches all `EntityState.Added` entities before rethrowing,
ensuring that failed chunk items are not silently re-saved by the catch-block save:

```csharp
catch
{
    foreach (var entry in dbContext.ChangeTracker.Entries()
                 .Where(e => e.State == EntityState.Added).ToList())
        entry.State = EntityState.Detached;
    throw;
}
```

This design enforces the invariant:

```
ProcessedLines + FailedLines == TotalLines
DeliveryItems.Count         == ProcessedLines
```

### Final status

After the chunk loop, the final job status is determined by the presence of failed chunks:

```csharp
var finalStatus = isBatch && job.FailedLines > 0
    ? JobStatus.PartiallySucceeded
    : JobStatus.Succeeded;
```

`PartiallySucceeded` is a terminal status that signals to consumers and operators that the import
completed but a subset of lines was not persisted. The job's `FailedLines`, `ProcessedLines`, and
`TotalLines` fields provide the exact breakdown.

### Chunk size

The default of **500 lines per chunk** is chosen as a balance across four constraints:

| Concern | Implication |
|---|---|
| DB round-trips | Fewer chunks → fewer transactions → lower overhead |
| Blast radius | Smaller chunks → fewer lines lost per failure |
| Memory | Larger chunks → higher peak EF tracking pressure |
| Transaction duration | Larger chunks → longer locks held in PostgreSQL |

At 500 lines, a 10,000-line file produces 20 chunks. Each chunk holds one PostgreSQL transaction
open for the duration of 500 `INSERT` statements — typically a few milliseconds on a healthy
database. The blast radius is bounded at 500 lines: a transient timeout affects at most 500 lines
before the pipeline recovers and continues.

`BatchOptions.ChunkSize` is configurable via the `Batch` configuration section. The default is
appropriate for deployments where delivery advice files are in the low-to-mid thousands of lines.

---

## Consequences

### Benefits

- **Bounded blast radius** — a transient failure during processing affects at most `ChunkSize` lines.
  Remaining chunks continue regardless, and the job transitions to `PartiallySucceeded` rather than
  `ProcessingFailed`.
- **Predictable memory usage** — EF Core tracks at most `ChunkSize` `DeliveryItem` entities at once.
  The change tracker is cleared implicitly after each successful `SaveChangesAsync`.
- **Correct progress tracking** — `ProcessedLines`, `FailedLines`, and `TotalLines` satisfy their
  invariant in all paths, including fault injection and transient infrastructure errors. Operators
  can observe meaningful progress for long-running imports.
- **No infrastructure changes** — sequential execution within a single scoped context requires no
  additional connection pool slots, no per-chunk transaction management, and no concurrency
  primitives.

### Trade-offs

- **Sequential throughput ceiling** — chunks cannot be parallelised without restructuring the
  pipeline's dependency graph. For very large files (100,000+ lines) on fast hardware, the DB
  write throughput may be the bottleneck.
- **PartiallySucceeded requires consumer awareness** — callers that only check `IsSuccess` on the
  `PipelineResult` will not detect partial failures; they must also inspect `job.Status` and the
  `FailedLines` counter.
- **Chunk failures are not retried** — a failed chunk is recorded and skipped. The pipeline does
  not retry individual chunks. If the infrastructure issue is transient, the entire job must be
  requeued, which will reprocess already-succeeded chunks (idempotency of `DeliveryItem` inserts
  is not guaranteed).

### When to revisit

- **Parallelise chunks** if per-file throughput becomes a bottleneck at realistic file sizes.
  This requires each chunk to use an independent scoped `DbContext` and a thread-safe counter
  for `ProcessedLines` / `FailedLines`.
- **Tune `ChunkSize`** if benchmark data (see ADR-015) shows that 500 is suboptimal for the
  actual file sizes in production. Increase for throughput; decrease for smaller blast radius.
- **Add per-chunk retry** if operational data shows that transient errors predominantly affect
  individual chunks rather than the entire database connection. A short retry loop around
  the chunk `SaveChangesAsync` call would reduce `PartiallySucceeded` occurrences without
  full-job requeue.
- **Expose `PartiallySucceeded` via API** if downstream consumers need to act on partial
  failures programmatically (e.g. trigger a compensating import for the failed lines only).
