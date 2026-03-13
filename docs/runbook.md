# Runbook — Ingestor

> This document describes operational procedures for investigating and resolving common issues. It is a living document — sections will be filled as the system matures.

---

## Quick Reference

| Action                 | Endpoint / Command               | Notes |
|------------------------|----------------------------------|-------|
| Check system health    | `GET /health`                    | TODO  |
| List failed jobs       | `GET /api/imports?status=`       | TODO  |
| Inspect job detail     | `GET /api/imports/{id}`          | TODO  |
| View job audit history | `GET /api/imports/{id}/history`  | TODO  |
| Requeue a failed job   | `POST /api/imports/{id}/requeue` | TODO  |
| Check worker heartbeat | TODO                             | TODO  |

---

## Investigating Dead-Lettered Jobs

### Symptoms

- Job status is `DeadLettered`
- All retry attempts exhausted

### Steps

1. **Identify the job** — TODO
2. **Check audit history** — TODO
3. **Read error details from last attempt** — TODO
4. **Determine root cause** — TODO
5. **Decide: requeue or discard** — TODO

### Common Causes

| Cause                        | Error Category | Resolution       |
|------------------------------|----------------|------------------|
| DB connection timeout        | Transient      | TODO             |
| Malformed CSV/JSON           | Permanent      | TODO             |
| Missing required fields      | Permanent      | TODO             |
| Duplicate idempotency key    | —              | TODO             |

---

## Investigating Stuck Jobs

### Symptoms

- Job has been in `Processing` for longer than expected
- Worker heartbeat is stale

### Steps

1. TODO

---

## Manual Requeue

### When to Requeue

- TODO

### When NOT to Requeue

- TODO

### Procedure

1. TODO

---

## Worker Not Processing Jobs

### Symptoms

- New jobs stay in `Received`
- Outbox entries remain `Pending`

### Steps

1. TODO

---

## Health Check Failures

### API Host

- TODO

### Worker Host

- TODO

---

## Log Investigation

### Finding Logs for a Specific Job

- TODO: Correlation ID lookup
- TODO: Serilog query patterns

### Common Log Patterns

| Pattern              | Meaning          |
|----------------------|------------------|
| TODO                 | TODO             |

---

## Appendix

### Status Model Reference

```
Received → Parsing → Validating → Processing → Succeeded
                                            ↘ ProcessingFailed → Retry → ...
                                                              → DeadLettered
                         ↘ ValidationFailed (terminal)
DeadLettered → Received (manual requeue)
```

### Error Categories

| Category    | Retryable | Description                  |
|-------------|-----------|------------------------------|
| `Transient` | Yes       | Infrastructure/timing issues |
| `Permanent` | No        | Data/validation issues       |
