# Ingestor — Fleetholm Logistics

A reliable import system for processing delivery advice files from multiple furniture and furnishing suppliers. Files are received, validated, processed, and tracked — with structured error handling, automatic retries, and full audit trails.

> **Note:** Fleetholm Logistics is a fictional company. This project is a portfolio demonstration of backend reliability patterns in .NET.

## What This Project Demonstrates

- Asynchronous background processing with database-backed job orchestration
- Retry logic with exponential backoff and dead-letter handling
- Idempotent file ingestion (no duplicate processing)
- Explicit status model with strict state transitions
- Structured error classification (transient vs. permanent)
- Full audit trail for every job lifecycle event
- Observability: OpenTelemetry tracing, structured logging, health checks
- Integration testing with Testcontainers

## Architecture

Two-process topology:

| Process         | Responsibility                                  |
|-----------------|-------------------------------------------------|
| **API Host**    | File upload, job registration, query endpoints  |
| **Worker Host** | Asynchronous job processing, retry, dead-letter |

Both processes share a PostgreSQL database. Job coordination uses a database-backed outbox pattern (no external message broker in V1).

```
┌──────────┐      ┌────────────┐     ┌────────────┐
│  Client  │────▶│  API Host  │────▶│ PostgreSQL │
└──────────┘      └────────────┘     └─────┬──────┘
                                           │
                                    ┌──────┴──────┐
                                    │ Worker Host │
                                    └─────────────┘
```

### Project Structure

```
src/
  Ingestor.Contracts/        Shared enums, DTOs, error contracts
  Ingestor.Domain/           Entities, value objects, domain events
  Ingestor.Application/      Use cases, pipeline steps, interfaces
  Ingestor.Infrastructure/   EF Core, outbox, observability setup
  Ingestor.Api/              ASP.NET Core Minimal API
  Ingestor.Worker/           BackgroundService for job processing

tests/
  Ingestor.Tests.Unit/       Parsers, validators, retry logic
  Ingestor.Tests.Integration/ Full pipeline tests with Testcontainers
  Ingestor.Tests.Architecture/ Layer dependency verification

docs/
  scope-v1.md               Project scope and requirements
  adrs/                     Architecture Decision Records
  runbook.md                Operational procedures
```

## Tech Stack

- .NET 10 / ASP.NET Core Minimal API
- PostgreSQL
- EF Core
- Docker Compose
- GitHub Actions
- OpenTelemetry
- Serilog
- Testcontainers
- xUnit / FluentAssertions

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-docker/)

### Run locally

```bash
docker compose up
```

The API will be available at `http://localhost:5000`. OpenAPI documentation is served via Scalar at `/scalar`.

### Run tests

```bash
dotnet test
```

Integration tests require Docker (Testcontainers will start a PostgreSQL instance automatically).

## API Overview

| Method | Endpoint                     | Description                  |
|--------|------------------------------|------------------------------|
| POST   | `/api/imports`               | Upload file and create job   |
| GET    | `/api/imports`               | List jobs (filterable)       |
| GET    | `/api/imports/{id}`          | Job detail with status       |
| GET    | `/api/imports/{id}/history`  | Full audit trail             |
| POST   | `/api/imports/{id}/requeue`  | Manually retry failed job    |
| GET    | `/health`                    | Health check                 |

## Documentation

- [Scope V1](docs/en/scope-v1-en.md)
- [Architecture Decision Records](docs/adrs/)
- [Runbook](docs/runbook.md)

## License

[MIT](LICENSE)
