# ADR-008: UUID v7 for Entity Identifiers

**Date:** 2026-03-19
**Status:** Accepted

---

## Context

Every domain entity requires a unique identifier. Three approaches were considered:

1. **Database-generated integer sequences** (`SERIAL` / `IDENTITY`) — the database assigns the ID on insert
2. **UUID v4** (`Guid.NewGuid()`) — randomly generated 128-bit identifier, assigned in application code
3. **UUID v7** (`Guid.CreateVersion7()`) — time-ordered 128-bit identifier, assigned in application code

The choice has consequences for domain model design, database index performance, and the pagination strategy available to the API layer.

---

## Decision

All entity identifiers use **UUID v7**, generated in application code via `Guid.CreateVersion7()`.

Each entity has a dedicated strongly-typed ID wrapping the `Guid`:

```csharp
public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new JobId(Guid.CreateVersion7());
}
```

EF Core is configured with `.ValueGeneratedNever()` for all ID properties, making clear that the database never generates or overrides the identifier:

```csharp
builder.Property(j => j.Id)
    .HasConversion(new ImportJobIdConverter())
    .ValueGeneratedNever();
```

---

## Consequences

### Benefits

- **Application-owned identity** — the ID exists before the entity is persisted. All related objects (`ImportPayload`, `OutboxEntry`, `AuditEvent`) can be constructed with the correct `JobId` in the same transaction, without a database round-trip between inserts. The identifier is a genuine part of the domain model, not a persistence detail assigned by the infrastructure layer.

- **Time-ordered, lexicographically sortable** — UUID v7 embeds a millisecond-precision Unix timestamp in its most significant bits. IDs generated later are always greater than IDs generated earlier. This property enables cursor-based pagination directly on the primary key column without an additional `CreatedAt` sort column (see ADR-009).

- **Better index performance than UUID v4** — random UUIDs (v4) distribute inserts uniformly across the B-tree index, causing frequent page splits and increased index fragmentation. UUID v7's near-sequential ordering keeps inserts at the trailing edge of the index, resulting in fewer splits and more efficient storage layout — the same property that makes auto-increment integers efficient, without requiring database-generated values.

- **Globally unique without coordination** — unlike auto-increment integers, UUID v7 requires no central sequence generator and is safe across multiple application instances, database shards, or environments. Two API pods can both generate `JobId.New()` simultaneously without collision risk.

- **Correlation and traceability** — because the ID is known before any I/O occurs, it can be attached to log entries, trace spans, and outbox entries from the moment a job is created. There is no gap between "job created in memory" and "job has a trackable identity".

### Trade-offs

- **Timestamp embedded in the ID** — UUID v7 IDs reveal rough creation timing to any client that receives them. For public-facing APIs where creation time is sensitive, this is a minor information leak. In this system, job IDs are not confidential and creation time is already exposed in the response body.

- **Monotonicity within the same millisecond is not guaranteed** — UUID v7 provides millisecond-precision ordering. Multiple IDs generated within the same millisecond may not be strictly ordered relative to each other. For cursor pagination this is acceptable: ties within a millisecond are resolved arbitrarily but consistently, and no records are skipped.

- **Requires .NET 9 or later** — `Guid.CreateVersion7()` was introduced in .NET 9. Projects targeting earlier runtimes would need a third-party library (e.g. `UUIDNext`). This codebase targets .NET 10, so there is no compatibility concern.

### When to revisit

- If the system is required to hide creation timing from external clients, replace UUID v7 with UUID v4 and introduce an explicit `CreatedAt` index column for ordering. Cursor pagination would need to switch from ID-based to timestamp-based comparison.
- If a third-party integration requires integer IDs, add a surrogate integer column while keeping UUID v7 as the internal domain identifier.
