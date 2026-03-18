# ADR-002: Idempotency Strategy for Import Job Creation

**Date:** 2026-03-18
**Status:** Accepted

---

## Context

The import upload endpoint (`POST /api/imports`) receives files from suppliers. In practice, clients retry failed HTTP requests — due to network timeouts, load balancer resets, or their own retry logic. Without a safeguard, each retry would create a duplicate `ImportJob`, causing the same file to be processed multiple times and potentially producing duplicate `DeliveryItem` records downstream.

The system needs to detect and suppress duplicate submissions while remaining transparent to well-behaved clients who simply retry a timed-out request.

---

## Decision

Each upload is assigned an **idempotency key** computed deterministically from the file content and the supplier identity:

```
IdempotencyKey = "{supplierCode}:{hex(SHA256(rawData))}"
```

Example: `ACME:3A7F2C1E...` (supplier code + colon + 64-character hex string)

On upload:
1. The key is computed before any database write.
2. If a job with that key already exists, the **existing job is returned with HTTP 200 OK**. No new job is created.
3. If no job exists, a new `ImportJob` is created and **HTTP 201 Created** is returned.

The key is stored on `ImportJob` with a `UNIQUE` database index as a final enforcement layer.

### Why content hash + supplier code?

A content hash alone is insufficient: two different suppliers legitimately sending the same file template (e.g. an empty delivery advice) should produce independent jobs. Prefixing the supplier code scopes deduplication to a single supplier's submission history.

The supplier code alone is also insufficient: the same supplier will submit different files over time. The hash captures the specific content of each submission.

### Why SHA256?

- **Deterministic** — the same bytes always produce the same hash, with no additional state or sequence number required.
- **Collision-resistant** — the probability of two distinct files producing the same SHA256 digest is approximately 2⁻¹²⁸, making accidental collisions negligible in practice.
- **Fast** — SHA256 over a typical delivery advice file (≤ 1 MB) completes in under 1 ms on commodity hardware.
- **No extra dependencies** — `System.Security.Cryptography.SHA256` is part of the .NET BCL.

### Why HTTP 200 OK for duplicates, not 409 Conflict?

A `409 Conflict` signals that the client did something wrong. A duplicate upload is not an error — it is an expected consequence of retry-safe clients. Returning `200 OK` with the existing job is the correct idempotent semantics: the same input produces the same observable outcome, regardless of how many times the request is sent.

---

## Consequences

### Benefits

- **Client transparency** — retrying a timed-out upload is safe by default. Clients do not need to implement deduplication logic on their side.
- **No sequence numbers or tokens required** — the key is derived purely from the request content. No pre-registration step (e.g. issuing an idempotency token) is needed.
- **Database enforcement** — the `UNIQUE` index on `idempotency_key` catches any race condition where two concurrent uploads with the same key slip past the application-level check simultaneously. One will receive a database constraint violation; the application will surface this as a conflict response.
- **Immutable after creation** — the key is set at job creation and never updated, making it safe to index and compare throughout the job lifecycle.

### Trade-offs

- **Content-sensitive** — a supplier who fixes a single character in their file will generate a new key and a new job. This is intentional (see edge cases), but means the system cannot detect "near-duplicate" corrections as resubmissions of the same logical delivery.
- **Raw bytes hashed** — the hash is computed over the raw file bytes, including line endings and encoding. A file re-exported with different line endings (`\r\n` vs `\n`) from the same source data will produce a different key and create a new job.
- **Key length** — the key format `{supplierCode}:{SHA256hex}` reaches up to 115 characters for a 50-character supplier code. The column is sized at `VARCHAR(128)` to accommodate this with minimal headroom.

### Edge cases

**Resubmission after correction:**
If a supplier corrects an error in their file and resubmits, the corrected file has different bytes, producing a different hash and a new job. The original (incorrect) job remains in the system. This is the desired behaviour: corrections are new submissions, not mutations of existing ones.

**Duplicate during active processing:**
If a duplicate is submitted while the original job is still being processed (e.g. status `Parsing`), the existing job is returned at its current status. The client can poll `GET /api/imports/{id}` to track progress.

**Requeued dead-lettered jobs:**
A dead-lettered job retains its original idempotency key. If the supplier resubmits the same file after dead-lettering, they receive **HTTP 409 Conflict** with error code `job.dead_lettered` — not a 200 OK, and not a new job. The response message includes the existing job ID so an operator can act on it via `POST /api/imports/{id}/requeue`.

This is intentional: `DeadLettered` is an operator-facing terminal state, not a client-resolvable one. Returning 200 OK would silently mislead the supplier into thinking their submission succeeded or is being retried. Returning 409 provides a clear, actionable signal.

An alternative considered was allowing a new job to be created by clearing the idempotency key on dead-lettering (Option C). This was rejected because it breaks idempotency semantics: multiple `ImportJob` records would exist for the same content, making it undefined which job to return for subsequent duplicate checks, and creating an unbounded retry chain outside operator control. If per-submission history is needed in the future, it requires a dedicated `submission_history` table rather than mutating the existing job's key.

### When to revisit

- If content-normalisation is required (e.g. treating `\r\n` and `\n` as equivalent), the key computation should hash a normalised representation rather than raw bytes.
- If idempotency windows are needed (e.g. "same file is allowed again after 30 days"), the `ExistsByIdempotencyKey` check must be scoped to a time window. This requires a schema change and updated query logic.
