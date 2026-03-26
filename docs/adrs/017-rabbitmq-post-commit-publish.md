# ADR-017: Post-Commit Publish for RabbitMqJobDispatcher

**Date:** 2026-03-26  
**Status:** Accepted

---

## Context

ADR-013 introduced `IJobDispatcher` as an Application-layer abstraction with two implementations:

- `DatabaseJobDispatcher` — writes an `OutboxEntry` into the EF change tracker. The write is lazy and transactional: the entry is not persisted until `IUnitOfWork.SaveChangesAsync` is called by the handler.
- `RabbitMqJobDispatcher` — calls `BasicPublishAsync` immediately when `DispatchAsync` is invoked. The publish is eager and non-transactional: it fires before `SaveChangesAsync`.

`CreateImportJobHandler` calls both in the order imposed by the outbox model:

```csharp
await jobRepository.AddAsync(job, payload, ct);
await jobDispatcher.DispatchAsync(job, ct);   // RabbitMQ: publishes immediately
await unitOfWork.SaveChangesAsync(ct);         // DB: job written after message already in queue
```

When the RabbitMQ worker runs in the same process (integration tests, benchmarks), it can consume the message before `SaveChangesAsync` completes. `GetByIdAsync` returns `null`, the message is nacked, routed to the dead-letter exchange, and the job is permanently stuck in `Received`. The same window exists in the retry path inside `RabbitMqWorker.HandleMessageAsync`.

This is a design-level defect, not a call-site ordering bug. The `IJobDispatcher` abstraction does not express *when* dispatch takes effect. Both implementations fulfill the interface contract but differ in a property — commit-relative timing — that no caller can observe or rely on. Fixing the call-site ordering would treat a symptom without addressing the root cause, and would break `DatabaseJobDispatcher` (which requires the `OutboxEntry` write to be batched into the same `SaveChangesAsync` call as the job).

---

## Decision

We introduce `IAfterSaveCallbackRegistry`, an `internal` Infrastructure interface that `EfUnitOfWork` implements alongside `IUnitOfWork`:

```csharp
// src/Ingestor.Infrastructure/Persistence/IAfterSaveCallbackRegistry.cs
internal interface IAfterSaveCallbackRegistry
{
    void OnAfterSave(Func<CancellationToken, Task> action);
}
```

`EfUnitOfWork` accumulates registered callbacks and executes them after `dbContext.SaveChangesAsync` completes:

```csharp
internal sealed class EfUnitOfWork(IngestorDbContext dbContext) : IUnitOfWork, IAfterSaveCallbackRegistry
{
    private readonly List<Func<CancellationToken, Task>> _afterSaveCallbacks = [];

    public void OnAfterSave(Func<CancellationToken, Task> action)
        => _afterSaveCallbacks.Add(action);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await dbContext.SaveChangesAsync(ct);
        foreach (var cb in _afterSaveCallbacks)
            await cb(ct);
        _afterSaveCallbacks.Clear();
    }
}
```

`RabbitMqJobDispatcher` injects `IAfterSaveCallbackRegistry` and registers the publish as a post-commit callback. `DispatchAsync` becomes synchronous — it plans the publish, it does not execute it:

```csharp
internal sealed class RabbitMqJobDispatcher(
    RabbitMqConnectionManager connectionManager,
    RabbitMqDeliveryTagStore deliveryTagStore,
    RabbitMqOptions options,
    IAfterSaveCallbackRegistry afterSaveCallbackRegistry) : IJobDispatcher
{
    public Task DispatchAsync(ImportJob job, CancellationToken ct = default)
    {
        afterSaveCallbackRegistry.OnAfterSave(async token =>
        {
            // ... BasicPublishAsync
        });
        return Task.CompletedTask;
    }
}
```

Both `IUnitOfWork` and `IAfterSaveCallbackRegistry` are registered in DI as aliases for the same scoped `EfUnitOfWork` instance, ensuring that the callback registered by `RabbitMqJobDispatcher` and the `SaveChangesAsync` called by the handler operate on the same object:

```csharp
services.AddScoped<EfUnitOfWork>();
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork>());
services.AddScoped<IAfterSaveCallbackRegistry>(sp => sp.GetRequiredService<EfUnitOfWork>());
```

### Why `IAfterSaveCallbackRegistry` in Infrastructure, not `OnAfterSave` on `IUnitOfWork`

The simpler alternative — adding `OnAfterSave` directly to `IUnitOfWork` — was explicitly rejected:

- `IUnitOfWork` is an Application-layer abstraction. Post-commit side-effect registration is an infrastructure concern driven entirely by the needs of one specific Infrastructure class (`RabbitMqJobDispatcher`). Placing it on the Application interface would leak that concern into the Application layer.
- Every mock of `IUnitOfWork` in unit tests would need to implement a method it never uses. The Interface Segregation Principle applies: callers that only call `SaveChangesAsync` should not be required to know about callback registration.
- `IAfterSaveCallbackRegistry` being `internal` enforces at compile time that no Application or Domain code can ever take a dependency on it.

### Why not reorder calls in callers

Changing `CreateImportJobHandler` to call `SaveChangesAsync` before `DispatchAsync` would fix the RabbitMQ race but break `DatabaseJobDispatcher`: the `OutboxEntry` write happens inside `DispatchAsync` and must be committed in the same transaction as the `ImportJob`. A second `SaveChangesAsync` after `DispatchAsync` would be required, adding a redundant round-trip and complicating the handler. Callers should not need to understand the commit-relative timing of the dispatcher they are injected with.

### Why not an outbox for RabbitMQ publish

Writing a "publish intent" row to a dedicated outbox table — committed atomically with the job — and draining it via a separate relay background service would provide true atomicity and resilience to RabbitMQ downtime at publish time. This is the correct approach if at-least-once publish guarantees are a hard requirement. For the current scope it is disproportionate: the `IAfterSaveCallbackRegistry` approach is sufficient because the DB transaction completes before any publish is attempted, and a publish failure propagates as an exception to the caller (who can retry the request).

---

## Consequences

### Benefits

- **Race condition eliminated** — the RabbitMQ publish always occurs after the DB transaction has committed. The worker cannot consume a message for a job that does not yet exist in the database.
- **Callers unchanged** — `CreateImportJobHandler`, `RequeueImportJobHandler`, and `RabbitMqWorker` required no modification. The ordering contract is now enforced by the infrastructure, not by callers.
- **`IUnitOfWork` remains minimal** — the Application-layer interface retains only `SaveChangesAsync`. No Application or Domain code is aware of the callback mechanism.
- **`DatabaseJobDispatcher` unchanged** — the Database strategy continues to write its `OutboxEntry` into the EF change tracker and commit atomically with the job, exactly as before.
- **ISP upheld** — `IAfterSaveCallbackRegistry` is injected only where it is used. Unit tests that mock `IUnitOfWork` require no changes.

### Trade-offs

- **Publish failure after commit** — if `BasicPublishAsync` throws after the DB transaction has already committed, the exception propagates out of `SaveChangesAsync`. The job exists in the database in `Received` status with no corresponding RabbitMQ message. The handler returns an error to the caller, who may retry — but because the job's idempotency key already exists, the retry returns the existing job (see ADR-002) rather than creating a new one and dispatching again. This leaves the job permanently undispatched unless an operator intervenes. This is an accepted limitation at the current scale; a full transactional outbox would close this gap.
- **Callback list is not thread-safe** — `List<T>` is not safe for concurrent writes. This is acceptable because `EfUnitOfWork` is scoped: one instance per DI scope, used sequentially within a single request or message handler. No concurrent access to the list can occur within a scope.
- **`_afterSaveCallbacks.Clear()` is essential** — if a scope calls `SaveChangesAsync` multiple times (e.g., pipeline intermediate saves), callbacks registered before the first save must not fire again on the second. The `Clear()` call after execution enforces this. Any future modification to `EfUnitOfWork` must preserve this invariant.

### When to revisit

- If publish-after-commit failures become observable in production (stranded `Received` jobs with no queue message), implement a transactional outbox for the RabbitMQ publish path as described in the "Why not an outbox" section above.
- If `EfUnitOfWork` is ever used in a concurrent context (e.g., parallel `Task.WhenAll` within a single scope), replace `List<T>` with a thread-safe collection.
