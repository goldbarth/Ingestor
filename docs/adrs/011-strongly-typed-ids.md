# ADR-011: Strongly-Typed Entity Identifiers

**Date:** 2026-03-19
**Status:** Accepted

---

## Context

Every domain entity requires a unique identifier. The underlying type — a `Guid` — is the same across all entities. Two approaches were considered for representing identifiers in method signatures and domain objects:

1. **Plain `Guid`** — identifiers are passed and stored as raw `Guid` values everywhere
2. **Strongly-typed wrapper** — each entity has a dedicated identifier type (e.g. `JobId`, `PayloadId`) that wraps the `Guid`

The choice affects compile-time safety, readability, and the amount of infrastructure boilerplate required.

---

## Decision

Each entity has a dedicated `readonly record struct` wrapping a `Guid`:

```csharp
public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new JobId(Guid.CreateVersion7());
}
```

The same pattern is applied to every entity: `PayloadId`, `OutboxEntryId`, `ImportAttemptId`, `DeliveryItemId`, `DeadLetterEntryId`, `AuditEventId`.

EF Core maps each type to its underlying `Guid` column via a dedicated value converter:

```csharp
internal sealed class ImportJobIdConverter() : ValueConverter<JobId, Guid>(
    id => id.Value,
    value => new JobId(value));
```

At the API boundary, route parameters arrive as raw `Guid` values and are wrapped immediately:

```csharp
// Endpoint
private static async Task<IResult> GetImportJobByIdAsync(Guid id, ...)
{
    var query = new GetImportJobByIdQuery(new JobId(id));
```

The raw `Guid` never travels deeper than the endpoint handler.

---

## Consequences

### Benefits

- **Compile-time ID safety** — a method that expects a `JobId` cannot receive a `PayloadId`. Passing identifiers in the wrong order is a compile error, not a silent runtime bug. This is especially valuable in handler signatures that accept multiple IDs:

  ```csharp
  // With plain Guid: argument order is invisible to the compiler
  Task AddAsync(Guid jobId, Guid payloadId)

  // With strongly-typed IDs: swapping arguments is a compile error
  Task AddAsync(JobId jobId, PayloadId payloadId)
  ```

- **Self-documenting signatures** — `GetByIdAsync(JobId id)` communicates exactly what kind of identifier is required. A caller looking at the signature knows without reading the implementation or documentation which entity is being queried.

- **Identifier is a domain concept** — `JobId` belongs to the domain model. A plain `Guid` is a data type with no domain meaning. Wrapping it makes the identifier a first-class citizen of the domain layer, consistent with DDD principles.

- **`readonly record struct` — zero overhead** — value semantics mean equality is by value, not by reference. The `struct` allocation is on the stack with no heap pressure. `record` generates `ToString()`, `Equals()`, and `GetHashCode()` correctly without manual implementation.

### Trade-offs

- **One value converter per entity** — EF Core requires an explicit `ValueConverter<TId, Guid>` for each ID type. With seven entity types, this produces seven near-identical converter classes. This is pure boilerplate: each file is three lines and follows an identical structure, but it must exist for EF Core to correctly persist and materialise the typed IDs.

- **`.Value` required in LINQ comparisons** — because EF Core translates `j.Id.Value > cursor.Value.Value` rather than `j.Id > cursor`, direct comparisons between typed IDs in LINQ queries require unwrapping. This is a minor but consistent friction point.

- **API boundary wrapping** — route and query parameters arrive as `Guid`. Every endpoint must explicitly construct the typed ID (`new JobId(id)`) at the boundary. This is intentional — the HTTP contract uses raw GUIDs — but it is a recurring manual step.

### When to revisit

- If the number of entities grows significantly, consider using a source generator or a base `StronglyTypedId<T>` record to eliminate the per-entity converter boilerplate.
- If a future version of EF Core adds native support for value object IDs, the individual converter classes can be removed.
