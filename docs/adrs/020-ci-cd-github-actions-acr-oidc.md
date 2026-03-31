# ADR-020: CI/CD with GitHub Actions, Azure Container Registry, and OIDC

**Date:** 2026-03-31   
**Status:** Accepted

---

## Context

v3.0.0 introduced deployment to Azure Container Apps but left the deployment process entirely manual: images were built and pushed to Docker Hub by hand, then the Container Apps were updated via CLI. This created two problems:

1. **Manual, error-prone process** — deploying required local credentials, a working Docker daemon, and a sequence of CLI commands executed in the correct order.
2. **Public image registry** — images were pushed to Docker Hub under a public account. The images themselves contain no secrets, but a private registry is the correct default for a production deployment.

---

## Decision

Deployment is automated via a GitHub Actions CD pipeline that triggers automatically after a successful CI run. Images are stored in **Azure Container Registry (ACR)** and deployed to Azure Container Apps.

### Pipeline structure

```
git push → CI (build, test, docker verify)
              ↓ only on success
           CD (build → push to ACR → deploy to ACA)
```

The CD workflow uses `workflow_run` to depend on CI:

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

### Authentication — OIDC (Workload Identity Federation)

GitHub Actions authenticates to Azure via a **Federated Identity Credential**, not a stored password. Azure trusts the GitHub OIDC provider; the workflow receives a short-lived token valid only for the duration of the run.

Required GitHub Secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. No passwords are stored.

The Service Principal holds two roles:
- `AcrPush` on the ACR — to push images
- `Contributor` on the resource group — to update Container Apps

### Image registry — ACR

Images are pushed to `<acr-name>.azurecr.io` instead of Docker Hub. Each image is tagged with the **full commit SHA** (`github.event.workflow_run.head_sha`):

```
acringestordev.azurecr.io/ingestor-api:a6fb1d805fce91ddb90b27f1110b629783576131
```

This makes every deployment fully traceable: the running image tag equals the exact git commit that produced it.

### Image pull — Managed Identity

Each Container App uses a **System-assigned Managed Identity** with the `AcrPull` role on the ACR. No registry credentials are stored on the Container App.

```bash
az containerapp identity assign --name <app> --resource-group <rg> --system-assigned
az role assignment create --assignee <principal-id> --role AcrPull --scope <acr-id>
az containerapp registry set --name <app> --resource-group <rg> \
  --server <acr-name>.azurecr.io --identity system
```

---

## Consequences

### Benefits

- A merge to `main` that passes CI is automatically deployed to Azure Container Apps without manual intervention.
- No long-lived secrets or passwords are stored anywhere. GitHub Secrets hold only non-sensitive IDs.
- Images are private by default.
- Every deployed revision is traceable to a specific git commit via the image tag.
- CD cannot run on broken code — the `workflow_run` gate enforces CI success.

### Trade-offs

- The CD pipeline rebuilds Docker images that CI already built and verified. This is redundant work but keeps the pipeline simple and avoids the complexity of artifact sharing across workflows.
- The `workflow_run` trigger cannot be dispatched manually with `gh workflow run`. Re-triggering a failed CD run requires `gh run rerun <run-id>`.

### Known pitfall — AAD propagation race condition

When assigning a Managed Identity and immediately creating a role assignment, Azure AD may not yet have propagated the new identity. The `role assignment create` command will fail with "Cannot find user or service principal". The fix is to wait briefly and retry the role assignment independently.

### When to revisit

- If the project migrates to Managed Identity for database access, the Data Protection connection string (ADR-019) can also be replaced with a `DefaultAzureCredential`-based client.
- If multiple environments (staging, production) are introduced, the `subject` claim in the Federated Credential and separate Container Apps environments should be added per environment.
