# ADR-009: Cursor-Based Pagination over Offset/Limit

**Date:** 2026-03-19
**Status:** Accepted

---

## Context

The `GET /api/imports` endpoint returns a paginated list of import jobs. Two standard approaches were considered:

1. **Offset/limit pagination** — `SELECT ... ORDER BY created_at LIMIT n OFFSET k`. The client passes a page number or offset; the database skips the first `k` rows and returns the next `n`.
2. **Cursor-based (keyset) pagination** — `SELECT ... WHERE id > cursor ORDER BY id LIMIT n`. The client passes the ID of the last item it received; the database seeks directly to that position in the index.

---

## Decision

We use **cursor-based pagination** with the entity's primary key (`JobId`) as the cursor.

```csharp
// Repository
if (cursor.HasValue)
    query = query.Where(j => j.Id.Value > cursor.Value.Value);

return await query
    .OrderBy(j => j.Id)
    .Take(pageSize)
    .ToListAsync(ct);
```

```csharp
// Response
public sealed record CursorPagedResponse<T>(
    IReadOnlyList<T> Items,
    Guid? NextCursor,
    bool HasNextPage);
```

The client receives `nextCursor` (the ID of the last item on the current page) and passes it as `?cursor=<guid>` to retrieve the next page. `nextCursor` is `null` on the final page.

This approach depends directly on UUID v7 (ADR-008): the `>` comparison is only semantically correct because UUID v7 IDs are time-ordered and lexicographically sortable. A random UUID v4 cursor would return arbitrary, inconsistent results.

---

## Consequences

### Benefits

- **Constant query performance** — offset/limit requires the database to count and skip `k` rows before returning results. At large offsets (`OFFSET 10000`) this becomes a full or near-full index scan. Cursor pagination uses a `WHERE id > cursor` predicate, which resolves to an index seek regardless of position — O(log n) for any page.

- **Stable results under concurrent inserts** — with offset pagination, a new job inserted on page 1 while a client reads page 2 causes every subsequent page to shift by one row. Records are duplicated or skipped silently. Cursor pagination is immune: the cursor is a position in sorted order, not a row count. New inserts before the cursor are invisible; inserts after it appear on future pages naturally.

- **No additional sort column** — cursor pagination requires a stable, ordered sort key. UUID v7 provides this natively via its embedded timestamp. Using UUID v4 or a surrogate integer would require an explicit `created_at` column and a composite index to guarantee stable ordering.

- **Simple implementation** — the repository implementation is a single `WHERE id > cursor` clause. There is no `ROW_NUMBER()`, no subquery, and no `OFFSET` arithmetic.

### Trade-offs

- **No random page access** — clients cannot jump to an arbitrary page (e.g. "go to page 42"). Navigation is strictly forward: each page yields a cursor for the next page only. This is acceptable for the job list use case, where consumers process jobs sequentially or search by status filter rather than navigating to a specific position.

- **Cursor is opaque** — the cursor value (a UUID) carries no human-readable meaning. Clients cannot infer how many total pages remain or what absolute position they are at. A `totalCount` field could be added to the response in a future version if needed.

- **Filter changes between pages are not protected** — if a client fetches page 1 with `?status=Received` and a job transitions status before page 2 is fetched, that job may be absent from page 2. This is a known limitation of all stateless pagination approaches and is acceptable for this use case.

### When to revisit

- Add a `totalCount` field to `CursorPagedResponse<T>` if clients need to display "showing X of Y results" UI.
- If bidirectional navigation (previous page) is required, extend the cursor to support both `id > cursor` (forward) and `id < cursor` (backward) queries.
