# ADR-021: CI/CD with GitHub Actions and Fly.io

**Date:** 2026-04-29  
**Status:** Accepted  
**Supersedes:** [ADR-020](020-ci-cd-github-actions-acr-oidc.md), [ADR-019](019-data-protection-azure-blob-storage.md)

---

## Context

ADR-020 established a CD pipeline using Azure Container Registry (ACR) and Azure Container Apps, authenticated via OIDC and Workload Identity Federation. ADR-019 persisted Data Protection keys in Azure Blob Storage to survive container restarts.

The Azure free trial expired, making Azure Container Apps no longer a viable hosting target. The project is a portfolio application that requires zero-cost cloud hosting with automatic deployments. Fly.io offers a free tier that covers three small machines and one persistent volume, which matches the deployment topology (API, Worker, Web).

---

## Decision

All three applications are deployed to **Fly.io** via a GitHub Actions CD pipeline. Authentication uses a single `FLY_API_TOKEN` secret. Images are built remotely by Fly.io's build infrastructure — no external container registry is required.

### Application topology

| App | Config | Auto-stop |
|---|---|---|
| `ingestor-api` | `fly.api.toml` | Yes (`stop`) |
| `ingestor-worker` | `fly.worker.toml` | No (always running) |
| `ingestor-web` | `fly.web.toml` | Yes (`stop`) |

All apps run in region `fra` (Frankfurt) on `shared-cpu-1x` / 256 MB machines.

### Pipeline structure

```
git push → CI (build, test, docker verify)
              ↓ only on success
           CD (migrate DB → deploy API → deploy Worker → deploy Web)
```

The CD workflow uses `workflow_run` to depend on CI, identical to the previous design:

```yaml
on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]
    branches: [main]

jobs:
  deploy:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
```

### Database migrations

EF Core migrations run as the first CD step, before any application is deployed. This ensures the schema is always current before new code starts:

```bash
dotnet ef database update \
  --project src/Ingestor.Infrastructure \
  --startup-project src/Ingestor.Api
```

The connection string is passed via the `DB_CONNECTION_STRING` GitHub secret.

### Authentication

GitHub Actions authenticates to Fly.io via a single long-lived API token stored as the `FLY_API_TOKEN` GitHub secret. The token is scoped to the Fly.io organisation.

This replaces the OIDC / Workload Identity Federation approach from ADR-020. Fly.io does not support OIDC federation with GitHub at this time.

### Deployment strategy — machine replacement

Each deployment destroys existing machines before creating new ones. This avoids machine accumulation on the free tier, which has a fixed machine count limit:

```bash
flyctl machines list --app <app> --json \
  | jq -r '.[].id' \
  | xargs -r -I{} flyctl machines destroy {} --app <app> --force

flyctl deploy --config fly.<app>.toml --remote-only --ha=false
```

`--ha=false` disables high-availability (two-machine default) to stay within free-tier limits.

### Data Protection keys — Fly.io persistent volume

`Ingestor.Web` uses ASP.NET Core Data Protection to encrypt antiforgery tokens. Keys must survive machine restarts.

Keys are persisted to a Fly.io **persistent volume** (`ingestor_web_keys`) mounted at `/data/keys`:

```toml
# fly.web.toml
[env]
  DataProtection__KeysPath = "/data/keys"

[[mounts]]
  source = "ingestor_web_keys"
  destination = "/data/keys"
```

```csharp
// Program.cs
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrEmpty(keysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("ingestor-web")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}
```

The volume is created once and reused across deployments. The CD pipeline checks for its existence before each Web deployment and creates it if missing:

```bash
flyctl volumes list --app ingestor-web --json \
  | jq -e '.[] | select(.name == "ingestor_web_keys")' \
  || flyctl volumes create ingestor_web_keys \
       --region fra --size 1 --app ingestor-web --yes
```

This replaces the Azure Blob Storage approach from ADR-019. The `PersistKeysToFileSystem` provider is part of the ASP.NET Core framework and requires no additional NuGet packages.

---

## Consequences

### Benefits

- A merge to `main` that passes CI is automatically deployed to all three Fly.io apps without manual intervention.
- No external container registry. Fly.io's remote builder handles image construction from the Dockerfile.
- Single secret (`FLY_API_TOKEN`) replaces three Azure credential IDs and the OIDC federation setup.
- Data Protection keys survive machine restarts and redeployments via the persistent volume. No cloud storage account is required.
- Zero hosting cost within Fly.io free-tier limits.

### Trade-offs

- `FLY_API_TOKEN` is a long-lived credential. If rotated, it must be updated in GitHub Secrets manually.
- The machine-destroy-before-deploy strategy introduces a brief downtime window per app during deployment. This is acceptable for a portfolio project.
- The Worker has `auto_stop_machines = false` to ensure continuous outbox polling. This keeps one machine running at all times and consumes free-tier machine hours.
- Fly.io volumes are single-region. Cross-region replication is not available on the free tier.

### When to revisit

- If Fly.io introduces OIDC federation support, replace `FLY_API_TOKEN` with a short-lived token.
- If zero-downtime deployments become a requirement, introduce blue/green or rolling deployment via `--strategy rolling` and remove the machine-destroy step.
- If the volume proves insufficient (e.g. key rotation produces many files), increase the volume size via `flyctl volumes extend`.
