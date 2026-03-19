# Branch Protection Rules – `main`

This document describes the recommended GitHub branch protection settings for the `main` branch.
Apply these via **Settings → Branches → Branch protection rules → Add rule** (pattern: `main`).

---

## Required Settings

### Require a pull request before merging

- **Enabled**: No direct pushes to `main`.
- **Required approvals**: 1 (adjust to team size).
- **Dismiss stale pull request approvals when new commits are pushed**: enabled.
  - Prevents approvals that predate new changes from remaining valid.
- **Require review from Code Owners**: optional, enable if a `CODEOWNERS` file is present.

### Require status checks to pass before merging

Enable **Require branches to be up to date before merging**, then add the following required checks:

| Check name                       | Job in `ci.yml`            |
|----------------------------------|----------------------------|
| `Build`                          | `build`                    |
| `Unit & Architecture Tests`      | `unit-tests`               |
| `Integration Tests`              | `integration-tests`        |
| `Docker Build Verification`      | `docker-build`             |

All four checks must pass for a PR to be mergeable.

### Restrict pushes

- **Allow force pushes**: disabled.
- **Allow deletions**: disabled.

---

## Rationale

- `--locked-mode` on `dotnet restore` in CI ensures lock files are always current; a stale lock file fails the `Build` job rather than silently regenerating.
- Docker build jobs (`docker-build`) run in parallel with `unit-tests`, providing fast feedback on image build failures without blocking test results.
- TRX artifacts uploaded on every run (including failures) make it possible to diagnose flaky tests directly from the PR checks page without re-running the workflow.
