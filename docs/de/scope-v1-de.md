# Scope V1 — Ingestor: Lieferdaten-Import-System

## Kernziel

Ein zuverlässiges Import-System, das Lieferdaten von externen Partnern entgegennimmt, validiert, verarbeitet und bei Fehlern sauber reagiert. Jeder Schritt ist nachvollziehbar, Fehler werden klassifiziert, Retries automatisch gesteuert — nichts geht verloren.

## Fachlicher Kontext

Ein fiktiver Einrichtungslogistik-Betreiber ("Fleetholm Logistics") koordiniert Lieferungen von mehreren Möbel- und Einrichtungslieferanten. Lieferanten senden täglich Lieferavis-Dateien mit Positionen: Artikelnummer, Produktname, Menge, voraussichtliches Lieferdatum und Referenznummer.

Die Herausforderung: Lieferanten liefern unterschiedliche Formate (CSV, JSON) und Qualitätsniveaus — manche Dateien sind sauber, manche haben fehlende Felder, Duplikate oder ungültige Werte. Möbellogistik bringt zusätzliche Komplexität: lange Lieferzeiten, Teillieferungen und häufige Terminverschiebungen.

> Der fachliche Kontext ist bewusst einfach gehalten. Der Wert des Projekts liegt nicht in der Domänenkomplexität, sondern in der technischen Zuverlässigkeit der Verarbeitung.

---

## Pflichtfunktionen

### Annahme (Intake)

- Importjob via API erstellen (POST mit Datei-Upload)
- CSV- und JSON-Dateien akzeptieren
- Rohdaten separat persistieren (Payload wird unverändert aufbewahrt)
- Idempotency-Key pro Upload (Hash aus Dateiinhalt + Lieferanten-ID)
- Duplikat-Erkennung vor Verarbeitung
- Sofortige Job-Registrierung mit Status `Received`

### Verarbeitung (Processing)

- Worker verarbeitet Jobs asynchron
- Parser-Stufe: CSV → internes Modell / JSON → internes Modell
- Validation-Stufe: Pflichtfelder, Wertebereich, Referenz-Plausibilität
- Processing-Stufe: Gemappte Daten in Zieltabelle (`DeliveryItem`) schreiben
- Jede Stufe aktualisiert den Job-Status
- Ergebnis persistieren (Anzahl verarbeiteter/fehlerhafter Zeilen)

### Fehlerbehandlung

- Fehlerkategorien: `Transient` (Retry-fähig) vs. `Permanent` (Dead Letter)
- Retry-Policy: max. 3 Versuche mit exponentiellem Backoff
- Jeder Versuch wird als `ImportAttempt` protokolliert
- Nach Retry-Erschöpfung → Status `DeadLettered`
- Manuelles Requeue über API-Endpoint
- Idempotente Job-Verarbeitung (gleicher Job darf nicht doppelt laufen)

### Transparenz (Audit & Tracking)

- Jede Statusänderung wird als `AuditEvent` persistiert
- Job-Historie vollständig rekonstruierbar
- Attempt-Historie mit Dauer, Ergebnis, Fehlerkategorie
- Fehlerursache strukturiert (nicht nur Freitext)
- Correlation ID pro Job durchgängig in Logs und Traces
- Requeue-Aktionen im Audit sichtbar

### Betrieb (Operations)

- Health Checks (DB-Verbindung, Worker-Heartbeat)
- Strukturierte Logs mit Serilog + Correlation ID
- OpenTelemetry Traces für die gesamte Pipeline
- Metriken-Endpoints: Jobs pro Status, durchschnittliche Verarbeitungszeit
- Docker Compose (API + Worker + PostgreSQL)
- CI Pipeline (GitHub Actions)
- Integration Tests mit Testcontainers

---

## Architektur

### Topologie — 2 Prozesse

| Prozess         | Rolle                       |
|-----------------|-----------------------------|
| **API Host**    | Annahme & Job-Registrierung |
| **Worker Host** | Asynchrone Verarbeitung     |

**Warum 2 Prozesse:**

- Entkopplung zwischen Annahme und Verarbeitung
- Realistische Runtime-Trennung (API kann unabhängig vom Worker skalieren/neustarten)
- Unterschiedliche Verantwortlichkeiten und Lifecycle
- Operatives Denken sichtbar für Reviewer

> Stärker als ein einzelner Prozess, aber noch klar beherrschbar. Kein Microservice-Overhead.

### Module

#### 1. Intake

- Importjob annehmen und registrieren
- Payload persistieren (Rohdaten)
- Idempotenz prüfen
- OutboxEntry für Worker erstellen

#### 2. Processing

- Job aus Outbox laden
- Parsing (CSV/JSON → internes Modell)
- Fachliche Validierung
- Mapping in Zieltabelle (`DeliveryItem`)
- Ergebnisstatus setzen

#### 3. Retry & Failure Handling

- Fehlerklassifikation (Transient/Permanent)
- Retry-Scheduling mit Backoff
- Dead-Letter-Übergang nach max. Attempts
- Manuelles Requeue

#### 4. Audit / Tracking

- Job-Statushistorie
- Attempt-Historie
- Zustandsübergänge als Events
- Fehlerdetails strukturiert

#### 5. Observability / Operations

- Structured Logging (Serilog)
- Distributed Tracing (OpenTelemetry)
- Metriken
- Health Checks
- Betriebsdiagnostik

---

## Statusmodell

Das Statusmodell ist explizit und streng — jeder Übergang ist definiert, es gibt keine impliziten Zustände.

### Zustände

| Status              | Bedeutung                                              |
|---------------------|--------------------------------------------------------|
| `Received`          | Job angelegt, Payload persistiert                      |
| `Parsing`           | Worker hat Job übernommen, Parsing läuft               |
| `Validating`        | Parsing erfolgreich, fachliche Validierung läuft       |
| `Processing`        | Validierung bestanden, Daten werden geschrieben        |
| `Succeeded`         | Verarbeitung abgeschlossen                             |
| `ValidationFailed`  | Permanenter Fehler — Daten sind fachlich ungültig      |
| `ProcessingFailed`  | Transienter Fehler — wird ggf. erneut versucht         |
| `DeadLettered`      | Alle Retries erschöpft oder manuell eskaliert          |

### Erlaubte Übergänge

```
Received → Parsing
Parsing → Validating
Parsing → ProcessingFailed (Parser-Fehler, transient)
Parsing → ValidationFailed (Datei grundlegend unlesbar)
Validating → Processing
Validating → ValidationFailed
Processing → Succeeded
Processing → ProcessingFailed
ProcessingFailed → Parsing (Retry)
ProcessingFailed → DeadLettered (max. Attempts erreicht)
DeadLettered → Received (manuelles Requeue)
```

> **Wichtig:** `ValidationFailed` ist ein Endzustand. Fachlich ungültige Daten werden nicht automatisch erneut versucht — nur manuelles Requeue nach Korrektur der Quelldaten.

---

## Messaging-Strategie (V1)

### Entscheidung: Datenbankbasierte Queue

Jobs werden in einer Outbox-Tabelle gespeichert, der Worker pollt.

| Vorteile                         | Nachteile                         |
|----------------------------------|-----------------------------------|
| Einfach, weniger Infrastruktur   | Kein echtes Messaging             |
| Transaktionale Konsistenz mit DB | Polling-Overhead                  |
| Fokus auf Zustandsmodell         | Weniger skalierbar bei hoher Last |

**Technische Umsetzung:**

- `OutboxEntry`-Tabelle mit Status `Pending` / `Processing` / `Done`
- Worker pollt mit konfigurierbarem Intervall
- `SELECT ... FOR UPDATE SKIP LOCKED` für Concurrency-Sicherheit
- Kein Job wird doppelt verarbeitet

> **V2-Perspektive:** RabbitMQ als Alternative, mit `IJobDispatcher`-Abstraktion und dokumentiertem Throughput-Vergleich (BenchmarkDotNet). Siehe `scope-v2.md`.

---

## Datenmodell

### `ImportJob`

| Feld               | Typ        | Beschreibung                              |
|--------------------|------------|-------------------------------------------|
| `Id`               | UUID       | Primärschlüssel                           |
| `SupplierCode`     | string     | Lieferanten-Kennung                       |
| `ImportType`       | enum       | `CsvDeliveryAdvice`, `JsonDeliveryAdvice` |
| `Status`           | enum       | Aktueller Jobstatus (siehe Statusmodell)  |
| `IdempotencyKey`   | string     | Hash aus Dateiinhalt + SupplierCode       |
| `PayloadReference` | string     | Referenz auf persistierte Rohdaten        |
| `ReceivedAt`       | timestamp  | Eingangszeit                              |
| `StartedAt`        | timestamp? | Verarbeitungsbeginn                       |
| `CompletedAt`      | timestamp? | Abschlusszeit                             |
| `CurrentAttempt`   | int        | Aktueller Versuchszähler                  |
| `MaxAttempts`      | int        | Maximale Versuche (Default: 3)            |
| `LastErrorCode`    | string?    | Letzter Fehlercode                        |
| `LastErrorMessage` | string?    | Letzte Fehlermeldung                      |

### `ImportAttempt`

| Feld             | Typ        | Beschreibung                |
|------------------|------------|-----------------------------|
| `Id`             | UUID       | Primärschlüssel             |
| `JobId`          | UUID       | FK → ImportJob              |
| `AttemptNumber`  | int        | Versuchsnummer              |
| `StartedAt`      | timestamp  | Startzeit                   |
| `FinishedAt`     | timestamp? | Endzeit                     |
| `Outcome`        | enum       | `Succeeded`, `Failed`       |
| `ErrorCategory`  | enum?      | `Transient`, `Permanent`    |
| `ErrorCode`      | string?    | Strukturierter Fehlercode   |
| `ErrorMessage`   | string?    | Fehlerbeschreibung          |
| `DurationMs`     | long       | Dauer in Millisekunden      |

### `ImportPayload`

| Feld          | Typ        | Beschreibung                   |
|---------------|------------|--------------------------------|
| `Id`          | UUID       | Primärschlüssel                |
| `JobId`       | UUID       | FK → ImportJob                 |
| `ContentType` | string     | `text/csv`, `application/json` |
| `RawData`     | text/bytes | Unveränderte Rohdaten          |
| `SizeBytes`   | long       | Dateigröße                     |
| `ReceivedAt`  | timestamp  | Eingangszeit                   |

### `DeliveryItem` (Zieltabelle)

| Feld              | Typ        | Beschreibung                     |
|-------------------|------------|----------------------------------|
| `Id`              | UUID       | Primärschlüssel                  |
| `JobId`           | UUID       | FK → ImportJob (Herkunft)        |
| `ArticleNumber`   | string     | Artikelnummer                    |
| `ProductName`     | string     | Produktbezeichnung               |
| `Quantity`        | int        | Menge                            |
| `ExpectedDate`    | date       | Voraussichtliches Lieferdatum    |
| `SupplierRef`     | string     | Referenznummer des Lieferanten   |
| `ProcessedAt`     | timestamp  | Zeitpunkt der Verarbeitung       |

### `DeadLetterEntry`

| Feld          | Typ        | Beschreibung                    |
|---------------|------------|---------------------------------|
| `Id`          | UUID       | Primärschlüssel                 |
| `JobId`       | UUID       | FK → ImportJob                  |
| `Reason`      | string     | Grund für Dead-Letter           |
| `FinalizedAt` | timestamp  | Zeitpunkt der Finalisierung     |
| `Snapshot`    | jsonb      | Job-Zustand zum Zeitpunkt       |

### `AuditEvent`

| Feld           | Typ       | Beschreibung                                     |
|----------------|-----------|--------------------------------------------------|
| `Id`           | UUID      | Primärschlüssel                                  |
| `JobId`        | UUID      | FK → ImportJob                                   |
| `EventType`    | string    | z.B. `StatusChanged`, `Requeued`, `DeadLettered` |
| `OldStatus`    | enum?     | Vorheriger Status                                |
| `NewStatus`    | enum?     | Neuer Status                                     |
| `Timestamp`    | timestamp | Zeitpunkt                                        |
| `TriggeredBy`  | string    | `System`, `Worker`, `API`                        |
| `MetadataJson` | jsonb     | Zusatzdaten                                      |

### `OutboxEntry`

| Feld          | Typ        | Beschreibung                    |
|---------------|------------|---------------------------------|
| `Id`          | UUID       | Primärschlüssel                 |
| `JobId`       | UUID       | FK → ImportJob                  |
| `Status`      | enum       | `Pending`, `Processing`, `Done` |
| `CreatedAt`   | timestamp  | Erstellungszeit                 |
| `LockedAt`    | timestamp? | Zeitpunkt der Übernahme         |
| `ProcessedAt` | timestamp? | Abschlusszeit                   |

---

## Technischer Stack

### Core

- .NET 8 (oder .NET 10 bei Release)
- ASP.NET Core Minimal API
- Worker Service (BackgroundService)
- PostgreSQL
- EF Core

### Runtime / Infra

- Docker Compose (API Host + Worker Host + PostgreSQL)
- GitHub Actions (Build, Test, Lint)
- OpenTelemetry (Traces + Metriken)
- Serilog mit strukturierter JSON-Ausgabe

### Tests

- xUnit
- FluentAssertions
- Testcontainers (PostgreSQL)

### API-Dokumentation

- Scalar (OpenAPI)
- ProblemDetails für alle Fehlerfälle

---

## Explizite Nicht-Ziele

- **Kein Frontend** — reine API
- **Kein Kubernetes** — Docker Compose reicht
- **Kein Cloud-Deployment** — lokal lauffähig
- **Kein Auth-System** — API Key oder einfacher Bearer Token
- **Kein Data Lake / Analytics**
- **Kein Echtzeit-Streaming**
- **Kein generisches Framework** — konkretes System für konkreten Use Case
- **Kein RabbitMQ in V1** — siehe Messaging-Strategie
- **Kein Multi-Region / Multi-Tenant**
- **Kein echter E-Mail-Versand** — Notification ist nicht im Scope

---

## Build-Reihenfolge

### Woche 1–2: Grundstruktur

- Solution-Struktur, Projekte, Docker Compose
- DB-Schema: `ImportJob`, `ImportPayload`, `OutboxEntry`
- POST-Endpoint: Datei hochladen → Job anlegen → Outbox schreiben
- GET-Endpoint: Job-Status abfragen
- Erster ADR: "Warum DB-Queue statt Message Broker"

### Woche 3–4: Verarbeitungs-Pipeline

- Parser für CSV und JSON
- Validator mit strukturierten Fehlern
- `DeliveryItem`-Tabelle befüllen
- Statusübergänge durchgängig
- Unit Tests für Parser + Validator

### Woche 5–6: Reliability

- Background Worker mit DB-Polling (`SELECT ... FOR UPDATE SKIP LOCKED`)
- Retry-Logic mit exponentiellem Backoff
- Dead-Letter-Mechanismus
- Manuelles Requeue
- Idempotenz-Prüfung
- ADR: Idempotenz-Strategie

### Woche 7–8: Observability & Audit

- OpenTelemetry Traces für Pipeline
- Serilog mit Correlation ID
- Health Checks
- Metriken-Endpoints
- `AuditEvent`-Tabelle + Schreiblogik
- Integration Tests mit Testcontainers

### Woche 9–10: Härtung & Dokumentation

- Failure-Tests: Was passiert bei DB-Ausfall während Processing?
- Edge Cases: Leere Datei, riesige Datei, ungültige Encoding
- CI/CD Pipeline finalisieren
- ADRs vervollständigen
- Runbook: "Was tun bei Dead-Lettered Jobs?"
- README mit Architekturübersicht

---

## Dokumentation (geplante ADRs)

| #   | Thema                                          |
|-----|------------------------------------------------|
| 001 | DB-Queue statt Message Broker in V1            |
| 002 | Idempotenz-Strategie                           |
| 003 | Pessimistisches Locking (SKIP LOCKED)          |
| 004 | Rohdaten separat persistieren                  |
| 005 | Fehlerkategorisierung: Transient vs. Permanent |
| 006 | Statusmodell-Design                            |

---

## V2-Ausblick

Siehe `scope-v2.md` (wird separat erstellt):

- RabbitMQ als Alternative zur DB-Queue
- `IJobDispatcher`-Abstraktion (DB + RabbitMQ)
- BenchmarkDotNet: Throughput-Vergleich DB-Polling vs. RabbitMQ
- Batch-Import (10.000+ Zeilen performant verarbeiten)
- ADR: Wann lohnt sich der Broker?
