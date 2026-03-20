# Scope V2 — Ingestor: Message Broker & Batch Processing

## Kernziel

V1 hat bewiesen, dass das System mit einer datenbankbasierten Queue zuverlässig funktioniert. V2 führt RabbitMQ als alternative Dispatch-Strategie ein — nicht als Ersatz, sondern als konfigurierbare Option. Zusätzlich wird Batch-Import für große Dateien implementiert.

Der Wert von V2 liegt nicht darin, dass RabbitMQ "besser" ist, sondern darin, dass die Entscheidung zwischen beiden Strategien mit echten Zahlen belegt wird.

---

## Was V2 zu V1 hinzufügt

| Bereich              | V1                              | V2                                         |
|----------------------|---------------------------------|--------------------------------------------|
| Job-Dispatch         | DB-Queue (Outbox + Polling)     | DB-Queue **oder** RabbitMQ (Config-Switch) |
| Abstraktion          | Direkt an OutboxEntry gekoppelt | `IJobDispatcher` Interface                 |
| Performance-Nachweis | —                               | BenchmarkDotNet: DB vs. RabbitMQ           |
| Batch-Verarbeitung   | Eine Datei = ein Job            | Eine Datei mit 10.000+ Zeilen = Batch-Job  |
| Infrastruktur        | PostgreSQL + API + Worker       | + RabbitMQ (optional via Docker Compose)   |

---

## Pflichtfunktionen

### 1. Dispatcher-Abstraktion

- `IJobDispatcher` Interface mit zwei Operationen: `Dispatch(job)` und `Acknowledge(job)`
- `DatabaseJobDispatcher` — kapselt die bestehende Outbox-Logik aus V1
- `RabbitMqJobDispatcher` — neue Implementierung
- Registrierung via DI, gesteuert durch Konfiguration (`appsettings.json`)
- Kein Feature-Flag-System, ein einfacher Config-Wert reicht: `Dispatch:Strategy = "Database"` oder `"RabbitMQ"`
- Beide Implementierungen sind jederzeit lauffähig

**Warum Config-Switch statt Ablösung:**
- In Production willst du den Broker testen können, bevor du umschaltest
- Der Benchmark-Vergleich braucht beide Implementierungen
- Es zeigt sauberes Interface-Design und Dependency Inversion

### 2. RabbitMQ-Integration

- RabbitMQ in Docker Compose ergänzen (mit Management UI)
- `RabbitMqJobDispatcher` publiziert Job-Events auf eine Queue
- Worker konsumiert von der Queue statt zu pollen
- Acknowledgement nach erfolgreicher Verarbeitung
- Dead-Letter-Exchange für fehlgeschlagene Messages konfigurieren
- Connection Handling: Reconnect bei Verbindungsabbruch

**Wichtige Abgrenzung:**
- Kein komplexes Exchange-/Routing-Setup — eine Queue, ein Consumer
- Kein eigenes Retry über RabbitMQ DLX — die bestehende Retry-Logik aus V1 bleibt
- RabbitMQ ist der Transport, nicht die Fehlersteuerung

### 3. Throughput-Benchmark

- BenchmarkDotNet-Projekt im Test-Ordner
- Szenarien:
  - 100 Jobs sequentiell
  - 1.000 Jobs sequentiell
  - 100 Jobs parallel (mehrere Worker)
- Gemessen wird: Durchsatz (Jobs/Sekunde), Latenz (Zeit von Dispatch bis Processing-Start), Ressourcenverbrauch (CPU, Memory)
- Ergebnisse dokumentiert im ADR (nicht nur Zahlen, sondern Interpretation)

### 4. Batch-Import

- Neue Import-Variante: Eine Datei mit vielen Zeilen (10.000+)
- Batch wird in Chunks verarbeitet (z.B. 500 Zeilen pro Chunk)
- Chunk-Größe konfigurierbar
- Fortschritt pro Batch nachvollziehbar: `ProcessedLines` / `TotalLines` auf dem Job
- Teilfehler möglich: Einige Chunks erfolgreich, andere fehlgeschlagen
- Batch-Status: `PartiallySucceeded` als neuer Status (nur für Batch-Jobs)
- Bestehende Single-File-Imports bleiben unverändert

**Abgrenzung:**
- Kein Streaming/Chunked Upload — die Datei wird komplett hochgeladen, dann in Chunks verarbeitet
- Kein paralleles Chunk-Processing in V2 — sequentiell pro Job, parallelisierung wäre V3-Thema

---

## Statusmodell-Erweiterung

Neuer Status nur für Batch-Jobs:

```
Processing → PartiallySucceeded (einige Chunks fehlgeschlagen)
```

`PartiallySucceeded` ist ein Endzustand. Der Job hat Ergebnisse produziert, aber nicht alle Zeilen waren erfolgreich. Die fehlgeschlagenen Zeilen sind über die Attempt-/Validation-Errors nachvollziehbar.

Alle bestehenden Status und Übergänge aus V1 bleiben unverändert.

---

## Datenmodell-Erweiterungen

### `ImportJob` — neue Felder

| Feld              | Typ    | Beschreibung                          |
|-------------------|--------|---------------------------------------|
| `TotalLines`      | int?   | Gesamtzahl Zeilen (nur bei Batch)     |
| `ProcessedLines`  | int?   | Bisher verarbeitete Zeilen            |
| `FailedLines`     | int?   | Fehlgeschlagene Zeilen                |
| `IsBatch`         | bool   | Kennzeichnung als Batch-Import        |
| `ChunkSize`       | int?   | Konfigurierte Chunk-Größe             |

> Nullable, weil bestehende Single-File-Imports diese Felder nicht nutzen.

---

## Technischer Stack — Ergänzungen zu V1

| Komponente      | Zweck                                 |
|-----------------|---------------------------------------|
| RabbitMQ 3.x    | Message Broker (optional)             |
| RabbitMQ.Client | .NET Client Library                   |
| BenchmarkDotNet | Throughput-Vergleich                  |
| Docker Compose  | Erweitert um RabbitMQ + Management UI |

---

## Explizite Nicht-Ziele

- **Kein komplexes Exchange-Routing** — eine Queue reicht
- **Kein RabbitMQ-basiertes Retry** — V1-Retry-Logik bleibt
- **Kein paralleles Chunk-Processing** — sequentiell pro Job
- **Kein Streaming-Upload** — Datei wird komplett hochgeladen
- **Keine Migration bestehender Jobs** — neue Jobs nutzen den konfigurierten Dispatcher
- **Kein Kafka, kein Azure Service Bus** — RabbitMQ reicht für den Lernzweck

---

## Build-Reihenfolge

### Phase 1: Dispatcher-Abstraktion (Woche 1)

- `IJobDispatcher` Interface definieren
- `DatabaseJobDispatcher` aus bestehender Outbox-Logik extrahieren
- DI-Registrierung mit Config-Switch
- Bestehende Tests laufen weiter (Regression)
- ADR: Dispatcher-Abstraktion und Config-Switch-Strategie

### Phase 2: RabbitMQ-Integration (Woche 2–3)

- RabbitMQ in Docker Compose
- `RabbitMqJobDispatcher` implementieren
- Worker um Queue-Consumer erweitern
- Dead-Letter-Exchange konfigurieren
- Connection Recovery bei Verbindungsabbruch
- Integration Test: Job über RabbitMQ dispatchen und verarbeiten

### Phase 3: Benchmark (Woche 3–4)

- BenchmarkDotNet-Projekt aufsetzen
- Benchmark-Szenarien implementieren
- Messungen durchführen
- ADR: DB-Queue vs. RabbitMQ — Ergebnisse und Empfehlung

### Phase 4: Batch-Import (Woche 4–6)

- Chunk-Logik im Processing-Step
- `TotalLines`/`ProcessedLines`/`FailedLines` auf ImportJob
- `PartiallySucceeded` Status
- Fortschritts-Endpoint: `GET /api/imports/{id}` zeigt Batch-Progress
- Unit Tests: Chunk-Splitting, Teilfehler
- Integration Test: 10.000-Zeilen-Datei verarbeiten

### Phase 5: Dokumentation (Woche 6)

- ADRs vervollständigen
- README aktualisieren (V2-Features, Konfiguration)
- Runbook ergänzen: RabbitMQ-Troubleshooting
- CHANGELOG

---

## Dokumentation (geplante ADRs)

| #   | Thema                                              |
|-----|----------------------------------------------------|
| 013 | Dispatcher-Abstraktion und Config-Switch-Strategie |
| 014 | RabbitMQ-Integration: Scope und Abgrenzung         |
| 015 | DB-Queue vs. RabbitMQ — Benchmark-Ergebnisse       |
| 016 | Batch-Import: Chunk-Strategie und Teilfehler       |
