# Platform Architecture — Phase 2A

## Current phase boundary

Phase 2A adds an independent central server without connecting it to the Windows Agent. The Phase 1/1.2 WPF implementation, local JSONL canonical record, derived CSV reports, 300-second screenshot default, and local retention remain unchanged.

```text
Visible Windows Agent (Phase 1/1.2, local only)
  -> local JSONL + CSV + PNG

Phase 2A central platform (not yet contacted by Agent)
  -> Next.js route handlers and server components
  -> Prisma driver adapter -> PostgreSQL
  -> private ScreenshotStorage
  -> authenticated Manager pages

ActivTrak (Phase 2C integration not implemented)
```

Agent transport, DPAPI credential storage, policy consumption, and offline queues belong to Phase 2B. ActivTrak webhooks belong to Phase 2C.

## Central server boundaries

- `server/app/api/v1` contains bounded device APIs. Enrollment has a dedicated environment token; every later device route requires matching device ID and per-device bearer secret.
- `server/lib` contains validation, cryptography, device/Manager authentication, database access, screenshot storage, retention, and dashboard query logic.
- `server/prisma` contains the schema and tracked migrations. Current process state is transactionally replaced instead of accumulated.
- `server/app/admin` uses server-side database access; Prisma is never exposed to client code.
- Screenshot binaries remain outside PostgreSQL and `public`; only confined storage keys and metadata are stored.
- ActivTrak models/status in Phase 2A are placeholders. No webhook or live API call exists.

Manager authorization is denied by default. Development mode requires two explicit flags and cannot run in production. Google mode uses Auth.js and requires an approved domain plus explicit Manager email membership.

## Windows Agent boundaries

The Phase 1 application is a normal, visible WPF process in the signed-in user's Windows session. Screenshot capture stays in that process because a future Windows Service must not attempt desktop capture across session boundaries. All Agent output remains on local disk; no network client exists in the Agent during Phase 2A.

```text
Visible WPF window
  -> MonitoringCoordinator
       -> process loop -> IProcessSnapshotProvider -> JSONL
                       -> IProcessReportWriter -> derived daily CSV
       -> screenshot loop -> IScreenshotCapture -> PNG + JSONL metadata
       -> RetentionCleanup -> configured data root only
```

Each loop executes one operation at a time and waits on a cancellable `PeriodicTimer`. The process and screenshot loops are independent, but an individual loop cannot overlap itself. Start/Stop and application shutdown cancel both loops.

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

### `src/Xugar.Endpoint.Agent`

Windows/WPF implementation:

- visible main window and development Start/Stop controls;
- .NET Generic Host configuration and dependency injection;
- resilient `System.Diagnostics.Process` enumeration;
- foreground-window identification;
- normal-input-desktop check before screenshots;
- Win32 monitor enumeration and GDI screen capture encoded as PNG;
- orchestration, UI progress, and operational events.

The executable manifest requests `asInvoker`, disables UI access, and declares per-monitor DPI awareness. It does not request elevation.

### `src/Xugar.Endpoint.Service`

An inert .NET Worker placeholder for a later approved phase. It logs that it is a placeholder and performs no monitoring, capture, installation, persistence, upload, or enforcement. The Phase 1 agent does not depend on it.

### `tests/Xugar.Endpoint.Tests`

xUnit tests cover validation, paths, naming, JSON/JSONL behavior, retention boundaries and cutoff, and loop cancellation/non-overlap. Windows desktop behavior remains a manual test because it requires an interactive session.

## Configuration

`appsettings.json` contains the committed policy defaults. The host also accepts command-line and environment configuration. Environment overrides intended for development use the `XUGAR_` prefix and .NET's double-underscore nesting convention, for example `XUGAR_Monitoring__ScreenshotIntervalSeconds=15`.

Validated ranges are:

- screenshot interval: 5 to 86,400 seconds;
- process interval: 5 to 86,400 seconds;
- retention: 1 to 8,760 hours;
- data root: a fully qualified, non-volume-root path after environment expansion.

The five-second minimum exists only to support controlled development testing. The committed screenshot default remains 300 seconds.

## Storage and retention safety

Telemetry is appended as one JSON object per line. Concurrent process and screenshot writers share a bounded semaphore, so records cannot interleave. Streams and process objects are disposed after each use.

JSONL is canonical. After a process snapshot is successfully persisted to JSONL, the process report writer updates three derived UTF-8 CSV files. Current and summary files use same-directory atomic replacement; event rows are serialized through a bounded writer gate. CSV failure is caught separately and cannot turn a successful raw process snapshot into a failed monitoring iteration.

Process events compare consecutive successful snapshots by PID. When both executable paths are available, the normalized paths must also agree; otherwise matching falls back to process name. The first snapshot in each agent run is a baseline. Categories are conservative path-derived human labels only and are not an enforcement input.

Cleanup compares file last-write timestamps with a UTC cutoff. It performs non-recursive deletes only after every candidate has been normalized and checked against the configured root. Directory reparse points and reparse-point files are skipped, preventing cleanup from following junctions or links outside the data tree. In-use and access-denied files are counted as failures instead of crashing monitoring.

## Screenshot boundary

Before enumerating monitors, the provider opens the current input desktop and requires its name to be `Default`. If the normal input desktop is unavailable—for example, while Windows is locked or a secure desktop is active—the iteration returns no images and records a local skipped event. It does not open, switch to, or bypass another desktop.

There is an unavoidable race if Windows changes desktops between the check and GDI capture. Windows protections remain authoritative; the application does not attempt to overcome a failed or blank capture.
