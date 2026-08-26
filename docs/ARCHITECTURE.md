# Platform Architecture — Phase 2B

## Current phase boundary

Phase 2B adds optional synchronization from the visible Agent to the Phase 2A server. The Phase 1/1.2 local JSONL canonical record, derived CSV reports, 300-second standalone screenshot default, screenshot protections, and local retention remain intact.

```text
Visible Windows Agent
  -> local JSONL + CSV + PNG (always first)
  -> bounded persistent queue
  -> authenticated Phase 2A APIs
  <- cached central monitoring policy

Central platform
  -> Next.js route handlers -> Prisma -> PostgreSQL
  -> private ScreenshotStorage
  -> authenticated Manager pages

ActivTrak (Phase 2C integration not implemented)
```

ActivTrak webhooks and ActivConnect remain Phase 2C and are not part of this data path.

## Central server boundaries

- `server/app/api/v1` contains bounded device APIs. Enrollment has a dedicated environment token; every later device route requires matching device ID and per-device bearer secret.
- `server/lib` contains validation, cryptography, device/Manager authentication, database access, screenshot storage, retention, and dashboard query logic.
- `server/prisma` contains the schema and tracked migrations. Current process state is transactionally replaced instead of accumulated.
- `server/app/admin` uses server-side database access; Prisma is never exposed to client code.
- Screenshot binaries remain outside PostgreSQL and `public`; only confined storage keys and metadata are stored.
- ActivTrak models/status in Phase 2A are placeholders. No webhook or live API call exists.

Manager authorization is denied by default. Development mode requires two explicit flags and cannot run in production. Google mode uses Auth.js and requires an approved domain plus explicit Manager email membership.

## Windows Agent boundaries

The application remains a normal, visible WPF process in the signed-in user's Windows session. Screenshot capture and Phase 2B networking stay in that process because a future Windows Service must not attempt desktop capture across session boundaries.

```text
Visible WPF window
  -> MonitoringCoordinator
       -> process loop -> IProcessSnapshotProvider -> JSONL
                       -> IProcessReportWriter -> derived daily CSV
       -> screenshot loop -> IScreenshotCapture -> PNG + JSONL metadata
       -> RetentionCleanup -> configured data root only
       -> AgentSynchronizationCoordinator
            -> installation identity + DPAPI credential
            -> central policy cache/schedule evaluator
            -> file-backed upload queue -> typed HTTP client
```

Each monitoring loop executes one operation at a time and uses cancellable delays. When synchronization is disabled, committed Phase 1 intervals apply unchanged. When enabled, a valid cached central policy supplies intervals and schedule permission. Missing/expired policy denies screenshots; local process snapshots continue safely but are not synchronized until policy is valid. Start/Stop and application shutdown cancel monitoring and synchronization loops.

## Projects

### `src/Xugar.Endpoint.Core`

Platform-light contracts and testable logic:

- device, process, screenshot, event, and configuration models;
- `IDeviceContextProvider`, `IProcessSnapshotProvider`, `IScreenshotCapture`, and `ILocalTelemetryStore`;
- settings validation and safe path construction;
- JSONL envelope serialization and file storage;
- RFC-compatible CSV formatting, lifecycle comparison, and daily process summaries;
- UTC screenshot filename generation;
- retention cutoff and cleanup;
- cancellable periodic task runner.
- atomic installation identity, protected-credential store abstraction, policy cache and schedule evaluation;
- typed server contracts, HTTP client, queue, mapping, backoff, and upload processor.

### `src/Xugar.Endpoint.Agent`

Windows/WPF implementation:

- visible main window and development Start/Stop controls;
- .NET Generic Host configuration and dependency injection;
- resilient `System.Diagnostics.Process` enumeration;
- foreground-window identification;
- normal-input-desktop check before screenshots;
- Win32 monitor enumeration and GDI screen capture encoded as PNG;
- orchestration, UI progress, and operational events.
- Windows DPAPI `CurrentUser` credential protection and compact synchronization status.

The executable manifest requests `asInvoker`, disables UI access, and declares per-monitor DPI awareness. It does not request elevation.

### `src/Xugar.Endpoint.Service`

An inert .NET Worker placeholder for a later approved phase. It logs that it is a placeholder and performs no monitoring, capture, installation, persistence, upload, or enforcement. The Phase 1 agent does not depend on it.

### `tests/Xugar.Endpoint.Tests`

xUnit tests cover Phase 1 regression plus identity, credential abstraction, API contracts/authentication, enrollment, policy/cache/schedule, queue durability/bounds/corruption, mapping, idempotent payload IDs, retry/backoff, and synchronization retention boundaries. Windows desktop and DPAPI OS behavior remain manual tests because they require an interactive Windows session.

## Configuration

`appsettings.json` contains the committed policy defaults. The host also accepts command-line and environment configuration. Environment overrides intended for development use the `XUGAR_` prefix and .NET's double-underscore nesting convention, for example `XUGAR_Monitoring__ScreenshotIntervalSeconds=15`.

Validated ranges are:

- screenshot interval: 5 to 86,400 seconds;
- process interval: 5 to 86,400 seconds;
- retention: 1 to 8,760 hours;
- data root: a fully qualified, non-volume-root path after environment expansion.

The five-second local minimum exists only to support controlled development testing. The committed standalone screenshot default remains 300 seconds. Phase 2B settings and aliases are documented in `PHASE2B_AGENT_SYNC.md`; HTTP is limited to explicit loopback development and production requires HTTPS.

## Storage and retention safety

Telemetry is appended as one JSON object per line. Concurrent process and screenshot writers share a bounded semaphore, so records cannot interleave. Streams and process objects are disposed after each use.

JSONL is canonical. After a process snapshot is successfully persisted to JSONL, the process report writer updates three derived UTF-8 CSV files. Current and summary files use same-directory atomic replacement; event rows are serialized through a bounded writer gate. CSV failure is caught separately and cannot turn a successful raw process snapshot into a failed monitoring iteration.

Process events compare consecutive successful snapshots by PID. When both executable paths are available, the normalized paths must also agree; otherwise matching falls back to process name. The first snapshot in each agent run is a baseline. Categories are conservative path-derived human labels only and are not an enforcement input.

Cleanup compares file last-write timestamps with a UTC cutoff. It performs non-recursive deletes only after every candidate has been normalized and checked against the configured root. Directory reparse points and reparse-point files are skipped, preventing cleanup from following junctions or links outside the data tree. In-use and access-denied files are counted as failures instead of crashing monitoring. The `sync` subtree is excluded from Phase 1 retention: credentials/identity/cache persist, while `FileUploadQueue` independently applies its configured age, item, and byte limits.

## Synchronization and idempotency

Heartbeat and current-process envelopes are replaceable and coalesced. Process/Agent events and screenshot captures receive client UUIDs before durable enqueue. Server uniqueness constraints on `(deviceId, clientEventId)` and `(deviceId, captureId)` make retries safe. Screenshot bytes are copied into private queue payload storage so local screenshot retention cannot invalidate a pending upload. Eviction is deterministic: oldest heartbeat/current state first, then Agent events, screenshots, and process events last. Any screenshot loss records a sanitized local queue-limit event.

## Screenshot boundary

Before enumerating monitors, the provider opens the current input desktop and requires its name to be `Default`. If the normal input desktop is unavailable—for example, while Windows is locked or a secure desktop is active—the iteration returns no images and records a local skipped event. It does not open, switch to, or bypass another desktop.

There is an unavoidable race if Windows changes desktops between the check and GDI capture. Windows protections remain authoritative; the application does not attempt to overcome a failed or blank capture.
