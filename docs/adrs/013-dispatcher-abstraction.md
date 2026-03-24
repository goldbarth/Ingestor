# ADR-013: Dispatcher Abstraction and Config-Switch Strategy

**Date:** 2026-03-24
**Status:** Accepted

---

## Context

In V1, all job dispatch logic was written directly against `OutboxEntry` and `IOutboxRepository`. Three separate call sites created `OutboxEntry` instances:

- `CreateImportJobHandler` — on initial job submission
- `RequeueImportJobHandler` — on manual requeue
- `Worker` — when scheduling a retry after a transient failure

The retry delay calculation (`RetryPolicy.CalculateDelay`) was also inlined in the Worker, making the outbox-specific scheduling logic visible at the orchestration level.

M6 introduces a RabbitMQ integration path (M7). If the outbox logic remained scattered across handlers and the Worker, adding a second dispatch backend would require modifying every call site. Each new backend would repeat the same invasive change.

Two approaches were considered:

1. **Replace the implementation** — remove `IOutboxRepository` from handlers and Worker entirely, substitute a new concrete class that wraps either outbox or RabbitMQ logic. Simpler upfront but closes the door on running both backends or switching at runtime.

2. **Introduce an interface** — define `IJobDispatcher` as an Application-layer abstraction. Callers depend only on the interface. Implementations (`DatabaseJobDispatcher`, future `RabbitMqJobDispatcher`) live in Infrastructure and are selected at startup.

---

## Decision

### Interface: `IJobDispatcher`

```csharp
public interface IJobDispatcher
{
    Task DispatchAsync(ImportJob job, CancellationToken ct = default);
    Task AcknowledgeAsync(ImportJob job, CancellationToken ct = default);
}
```

The interface lives in the **Application layer** and accepts only `ImportJob` — a Domain type. No `OutboxEntry`, no `OutboxEntryId`, no infrastructure concepts cross the boundary.

`DatabaseJobDispatcher` (Infrastructure) is the sole implementation for V1/M6. It encapsulates all `OutboxEntry` construction, including:

- Attempt number derivation from `job.CurrentAttempt`
- Retry delay scheduling via `RetryPolicy.CalculateDelay` for `CurrentAttempt > 0`
- Marking processing complete via `IOutboxRepository.MarkAsDoneByJobAsync`

`AcknowledgeAsync` identifies the outbox entry to complete by querying for the single `Processing`-status entry with the matching `JobId`. This is safe because `ClaimNextAsync` uses `FOR UPDATE SKIP LOCKED`, guaranteeing at most one `Processing` entry per job at any time.

### Config-switch: `Dispatch:Strategy`

```json lines
"Dispatch": {
  "Strategy": "Database"
}
```

`AddInfrastructure` reads this key from `IConfiguration` and registers the matching `IJobDispatcher` implementation. Accepted values: `Database`, `RabbitMQ` (implemented in M7). Unknown or missing values fall back to `Database`; a warning is emitted via `app.Logger` after `builder.Build()` — at which point Serilog is fully configured.

Both the API host and the Worker host call `AddInfrastructure` with the same configuration instance, ensuring the strategy is consistent across both processes.

### Why an interface instead of replacing the implementation

Replacing the implementation would require editing `CreateImportJobHandler`, `RequeueImportJobHandler`, and `Worker` for every new backend. The Open/Closed Principle applies here: handlers should be closed for modification when a new dispatch mechanism is introduced. The `IJobDispatcher` interface achieves this — all three call sites are now stable regardless of how many backends are added.

An additional benefit: both backends coexist in the binary, selectable at runtime via configuration. This enables zero-downtime strategy switches and simplifies benchmarking (M8).

### Why config-switch instead of feature flags

Feature flags typically require a flag evaluation service, a client library, and often an external store. The overhead is justified when flags need to change at runtime without redeployment, or when different users/tenants receive different behavior.

Neither condition applies here. The dispatch strategy is a deployment-time decision — it changes when infrastructure changes (e.g., provisioning a RabbitMQ broker), not in response to user activity. A plain `IConfiguration` key is sufficient, reads from both `appsettings.json` and environment variables without additional dependencies, and is immediately visible to operators inspecting the configuration.

---

## Consequences

### Benefits

- **Handlers and Worker are stable** — no code in `CreateImportJobHandler`, `RequeueImportJobHandler`, or `Worker` needs to change when M7 introduces `RabbitMqJobDispatcher`.
- **Outbox logic is centralized** — `OutboxEntry` construction and retry scheduling live exclusively in `DatabaseJobDispatcher`. No other class creates `OutboxEntry` instances.
- **Operational simplicity** — a single configuration key controls the dispatch backend for both hosts. Environment variable override (`Dispatch__Strategy=RabbitMQ`) works without code changes.
- **Fallback is safe** — an unknown strategy value defaults to the known-good `Database` implementation with a logged warning rather than a startup failure.

### Trade-offs

- **`IConfiguration` on `AddInfrastructure`** — the extension method now requires an `IConfiguration` parameter in addition to the connection string. Test fixtures must construct a minimal `ConfigurationBuilder().Build()` instance. This is a small but real increase in call-site complexity.
- **Unregistered `IJobDispatcher` during M7 development** — setting `Dispatch:Strategy=RabbitMQ` before `RabbitMqJobDispatcher` is implemented leaves `IJobDispatcher` unregistered, causing a runtime exception on first resolution. This is an intentional placeholder; the risk is limited to development environments where the config is explicitly changed.
- **`AcknowledgeAsync` performs an additional DB query** — identifying the `Processing` outbox entry by `JobId` requires a `SELECT` before the `UPDATE`. This replaces the direct `MarkAsDoneAsync(OutboxEntryId)` call that the Worker previously used. The cost is one indexed query per job completion and is negligible at the current scale.

### When to revisit

- When `RabbitMqJobDispatcher` is implemented (M7), the empty `if` branch in `AddInfrastructure` must be filled. The placeholder comment marks the location.
- If the strategy ever needs to change without redeployment (e.g., live traffic migration from DB queue to RabbitMQ), a feature flag or dynamic configuration source would be the appropriate upgrade path.
