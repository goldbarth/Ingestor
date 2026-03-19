# ADR-004: Raw Payload Persistence in a Separate Entity

**Date:** 2026-03-19  
**Status:** Accepted

---

## Context

When a supplier uploads a file, the system must retain the original byte content to support processing. Two structural options were considered:

1. **Embed in `ImportJob`** — store `byte[] RawData` and metadata directly on the aggregate root
2. **Separate `ImportPayload` entity** — store the file bytes and metadata in a dedicated table with a foreign key to `ImportJob`

The choice affects query performance, domain model clarity, retry behaviour, and long-term auditability.

---

## Decision

We store the raw file bytes in a **dedicated `ImportPayload` entity** that is persisted in the same transaction as the `ImportJob` but held in a separate table (`import_payloads`).

```csharp
public sealed class ImportPayload
{
    public PayloadId Id { get; private set; }
    public JobId JobId { get; private set; }
    public string ContentType { get; private set; }
    public byte[] RawData { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
}
```

The `ImportPipelineHandler` loads the payload explicitly via `GetPayloadByJobIdAsync` only when parsing is required. No other code path loads the raw bytes.

---

## Consequences

### Benefits

- **Retry reliability** — On a transient failure, the worker retries the job using the persisted raw bytes. There is no dependency on the original upload request being available. The payload survives process restarts, worker crashes, and arbitrary delays between receipt and processing.

- **Separation of concerns** — `ImportJob` models the *processing lifecycle* (a state machine over time). `ImportPayload` models the *immutable input artefact* (the bytes as received). These have different identities, different access patterns, and different lifetimes. Conflating them into one aggregate would blur the boundary between "what happened" and "what was given to us".

- **Query efficiency** — `ImportJob` is the hot entity in the system: it is read by every status query, list endpoint, metrics query, and audit navigation. Embedding a potentially large `byte[]` in `ImportJob` would cause every such read to load payload data unnecessarily, or require lazy-loading configuration and the cognitive overhead that comes with it. Keeping the job row small ensures fast reads on the common path.

- **Auditability** — The original file is preserved exactly as received, byte-for-byte, independent of whether processing succeeded or failed. In a dispute with a supplier, the raw payload can be retrieved and compared against the processed `DeliveryItem` records to verify correctness.

### Trade-offs

- **Additional join on retry** — The worker must issue a second query (`GetPayloadByJobIdAsync`) before it can parse. This is a deliberate, explicit load rather than an automatic include, which means a developer must know to load the payload separately when needed.

- **Storage growth** — Raw payloads are never deleted in V1. For high-volume deployments this will grow the `import_payloads` table significantly. A retention policy or archival strategy will be needed if payload size or volume increases.

- **Transactional coupling at write time** — `ImportJob` and `ImportPayload` must be written atomically. This is enforced by the `IImportJobRepository.AddAsync` signature, which takes both as parameters. If this convention is not followed, a job could exist without a payload, causing a null-reference failure on first processing attempt.

### When to revisit

- If payloads grow very large (hundreds of MB) and PostgreSQL is no longer an appropriate store, migrate `RawData` to blob storage (e.g. Azure Blob, S3) and replace the byte array with a URI reference. The `ImportPayload` entity structure accommodates this change: only `RawData` is replaced, all other fields remain.
- If a retention policy is required, add a `RetainUntil` field to `ImportPayload` and introduce a background cleanup job.
