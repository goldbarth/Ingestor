# ADR-019: Persistent Data Protection Keys via Azure Blob Storage

**Date:** 2026-03-30  
**Status:** Accepted

---

## Context

Blazor Server uses ASP.NET Core Data Protection to encrypt antiforgery tokens. These tokens are embedded in forms and validated on each submission. The Data Protection system requires that the encrypting and decrypting keys are the same across the lifetime of a user session.

By default, Data Protection keys are stored **in memory**. This is sufficient for a single persistent process. It breaks under two conditions:

1. **Multiple instances** — different instances hold different in-memory keys; a request routed to a different instance than the one that rendered the form fails token validation.
2. **Process restart** — any restart (crash recovery, redeployment, or scale-to-zero cold start) generates new keys; tokens encrypted by the previous process become invalid.

When deploying `Ingestor.Web` to **Azure Container Apps**, both conditions apply:

- Azure Container Apps defaults to **scale-to-zero**: the container is stopped when there is no traffic and restarted on the next request. Each restart is a new process with new in-memory keys.
- The result: after a period of inactivity, the first user interaction that submits a form (e.g. the file upload on the Imports page) silently fails. The upload button appears active but produces no response. Browser DevTools shows no network error; the Blazor circuit is functional, but the antiforgery token is rejected server-side.

---

## Decision

When `DataProtection:StorageConnectionString` is present in configuration, Data Protection keys are persisted to **Azure Blob Storage** (`dataprotection` container, `keys.xml` blob).

```csharp
var storageConnectionString = builder.Configuration["DataProtection:StorageConnectionString"];
if (!string.IsNullOrEmpty(storageConnectionString))
{
    var containerClient = new BlobContainerClient(storageConnectionString, "dataprotection");
    containerClient.CreateIfNotExists();
    builder.Services.AddDataProtection()
        .SetApplicationName("ingestor-web")
        .PersistKeysToAzureBlobStorage(containerClient.GetBlobClient("keys.xml"));
}
```

**The configuration key is opt-in.** When absent (local development, Docker Compose), the application falls back to the default in-memory key storage. No existing setup is affected.

### Why Azure Blob Storage

The deployment target is Azure Container Apps. Azure Blob Storage is:

- Already required as infrastructure (the storage account is created for the deployment anyway).
- Low-cost and low-maintenance for a single small XML file.
- Natively supported by the `Azure.Extensions.AspNetCore.DataProtection.Blobs` package, which integrates with the standard `IDataProtectionBuilder` API.

Alternatives considered:

| Option | Reason not chosen |
|---|---|
| Azure Key Vault | Disproportionate for key storage alone; adds managed identity or certificate requirements |
| Shared file volume | Not natively supported by Azure Container Apps without additional configuration |
| Redis / distributed cache | Adds another required infrastructure dependency |
| Disable antiforgery | Not acceptable; antiforgery protection must be preserved |

### `SetApplicationName`

`SetApplicationName("ingestor-web")` is set explicitly. Without it, the application discriminator defaults to the content root path, which varies across containers and would cause cross-instance key isolation even when keys are stored in the same blob.

---

## Consequences

### Benefits

- Scale-to-zero restarts no longer invalidate active user sessions or pending form submissions.
- Multiple replicas (if ever enabled) share the same key ring.
- No impact on local development or non-Azure deployments.

### Trade-offs

- Azure deployments require a Storage Account and a `dataprotection` container. This is documented in the runbook and CHANGELOG migration notes.
- The connection string must be stored as a Container Apps secret and referenced via `secretref:` — the plain connection string must not appear as a plain-text environment variable.

### When to revisit

- If the deployment target changes to a platform with a native key store (e.g. Kubernetes with a secrets store CSI driver), replace `PersistKeysToAzureBlobStorage` with the appropriate provider.
- If Managed Identity is configured for the web container, replace the connection-string-based `BlobContainerClient` with a `DefaultAzureCredential`-based client to eliminate the stored secret.
