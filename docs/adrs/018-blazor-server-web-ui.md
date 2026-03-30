# ADR-018: Blazor Server as Web Frontend

**Date:** 2026-03-30  
**Status:** Accepted

---

## Context

The system required a web-based UI to expose three operational views:

- **Dashboard** — real-time job and processing metrics
- **Imports** — file upload with live job status tracking
- **Dead Letters** — list of failed jobs with requeue capability

The existing stack is .NET 10 with ASP.NET Core. A frontend technology had to be chosen.

The candidates considered were:

| Option | Description |
|---|---|
| **Blazor Server** | Server-side rendering with SignalR circuit; UI logic runs on the server in .NET |
| **Blazor WebAssembly** | Client-side .NET runtime downloaded to the browser; hosted as static files |
| **React / Vue / Angular** | JavaScript SPA with a dedicated build pipeline; communicates with the API via HTTP |
| **Razor Pages / MVC** | Server-rendered HTML; no persistent client connection |

---

## Decision

We use **Blazor Server**.

The deciding factors:

1. **No additional build tooling.** No Node.js, no npm, no bundler. The frontend is built as part of the standard `dotnet build` / `dotnet publish` pipeline. This keeps the CI/CD setup uniform across all three services.

2. **Direct .NET service access.** The `IngestorApiClient` typed HTTP client can be registered and injected into Blazor components exactly like any other ASP.NET Core service. No JSON bridge, no token passing, no CORS configuration for the UI layer.

3. **Real-time updates without manual polling.** The SignalR circuit maintained by Blazor Server allows server-initiated UI updates. Live job status changes on the Imports page require no client-side polling loop.

4. **No separate hosting model.** Blazor Server runs as a standard ASP.NET Core process and is containerised identically to the API and Worker. Blazor WebAssembly would require a separate static file host or a hosted model that adds complexity.

Blazor Server's known trade-offs were accepted:

- **Server memory per connected user** — acceptable for an internal operations tool with a small number of concurrent users.
- **SignalR dependency** — the persistent circuit requires a stable connection; brief disconnects trigger an automatic reconnect. Acceptable for operational tooling; not suitable for high-latency or offline scenarios.
- **Scale-to-zero behaviour** — see [ADR-019](019-data-protection-azure-blob-storage.md).

---

## Consequences

### Benefits

- Single build pipeline for all three services; no JS toolchain in CI.
- Full .NET type safety end-to-end: DTOs from `Ingestor.Contracts` are used directly in Blazor components.
- Real-time UI state without client-side state management.

### Trade-offs

- Blazor Server is not suitable for public-facing, high-traffic frontends. For this internal operations tool, the concurrency limits are not a constraint.
- Static hosting (CDN, blob storage) is not possible; the web process must always be running to serve the UI.
- Scale-to-zero in cloud environments (e.g. Azure Container Apps) requires persistent Data Protection key storage to prevent circuit/antiforgery failures after container restarts. Addressed in [ADR-019](019-data-protection-azure-blob-storage.md).
