# Xugar Endpoint Monitor

Xugar Endpoint Monitor is a transparent, company-authorized endpoint management and monitoring platform for company-owned Windows laptops. The repository contains the stable Phase 1/1.2 visible Windows Agent, the Phase 2A central platform, and the Phase 2B optional Agent synchronization layer.

Xugar owns device health, approved periodic screenshots, complete process presence, and START/STOP events. ActivTrak remains the future source of truth for application/website activity, usage duration, active/passive status, productivity classification, and workforce analytics. Process presence must never be presented as employee activity or hours worked.

## Phase 1 capabilities

- Visible WPF status window with Start and Stop controls.
- Process snapshots every 60 seconds by default.
- PNG screenshots every 300 seconds (five minutes) by default.
- One screenshot per detected monitor when the normal interactive desktop is available.
- Local JSONL process, screenshot-metadata, operational, and retention records.
- Derived human-readable daily process CSV reports.
- Local file retention of 24 hours by default.
- Resilient handling of inaccessible or short-lived processes.

The Agent does not manage devices remotely, enforce application policy, install a Windows Service, start automatically, hide itself, capture command lines, keylog, capture the clipboard, collect user credentials, activate a microphone or webcam, or bypass UAC/secure desktop.

## Phase 2A central-platform capabilities

- Next.js App Router server written in strict TypeScript.
- PostgreSQL 18 development service with tracked Prisma migrations.
- Token-gated device enrollment with stable installation IDs and hash-only rotating device secrets.
- Authenticated heartbeat, current-process replacement, lifecycle-event, screenshot, Agent-event, and policy APIs.
- Private filesystem screenshot storage with server-generated keys, MIME/signature/size validation, and SHA-256 metadata.
- Screenshot retention command confined to the configured storage root.
- Server-rendered Manager overview, device list, and device detail pages.
- Manager authentication defaults to denied, with explicit local-development and Google Workspace/Auth.js boundaries.
- ActivTrak configuration/schema placeholders only; webhook ingestion remains Phase 2C.

## Phase 2B Agent synchronization

- Synchronization is disabled by default; Phase 1 local-only operation needs no server.
- A random installation GUID is persisted independently of user, network, and hardware identifiers.
- Enrollment exchanges the bootstrap token once for a per-device secret. The secret is protected with Windows DPAPI `CurrentUser` and is never written to JSONL, CSV, settings, or logs.
- Heartbeat and newest current-process state are coalesced; process events, screenshots, and bounded Agent events retain stable client UUIDs for retry idempotency.
- A durable bounded file queue survives Agent/server restarts, uses atomic same-directory writes, applies exponential backoff with jitter, and keeps screenshot payload copies private under the Agent data root.
- Central policy is cached with a local retrieval time. In synchronized mode, an unavailable, invalid, or expired policy denies new screenshots. Valid policy toggles and schedule windows gate screenshot/process collection. Local process telemetry continues safely while policy is temporarily unavailable.
- The WPF window remains visible and shows enrollment, server, queue, upload, and policy status without exposing secrets.

Phase 2B follows `LOCAL FIRST -> NETWORK SECOND`: canonical JSONL, derived CSV, and local PNG persistence complete before any eligible queue operation is created.

## Prerequisites

- Windows 11 x64
- .NET 10 SDK
- Node.js 24 and npm
- Docker Desktop with Docker Compose, for development PostgreSQL only

Confirm the SDK with:

```powershell
dotnet --info
```

## Build and test

From the repository root:

```powershell
dotnet restore
dotnet build Xugar.EndpointMonitor.sln -c Debug
dotnet test Xugar.EndpointMonitor.sln -c Debug --no-build
```

For the central server and its isolated integration-test database:

```powershell
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml --profile test up -d postgres-test
Copy-Item .\server\.env.example .\server\.env
# Replace every placeholder token/secret in server\.env before use.
Set-Location .\server
npm ci
npm run prisma:migrate:deploy
npm run lint
npm test
npm run build
```

Stop development containers without deleting the named database volume:

```powershell
docker compose -f docker-compose.dev.yml down
```

Do not add `--volumes` without explicit approval.

`npm test` explicitly loads committed local-only `server/.env.test`, verifies that its PostgreSQL host is loopback and its database name ends in `_test`, and deploys tracked migrations before Vitest. Developers do not set `DATABASE_URL` manually. The test suite deletes records only in the dedicated `xugar_endpoint_test` database on loopback port 55433.

### Manager authentication

The safe default is `XUGAR_MANAGER_AUTH_MODE=disabled`. Local UI development requires both `XUGAR_MANAGER_AUTH_MODE=development` and `XUGAR_DEVELOPMENT_MANAGER=true`, and is refused when `NODE_ENV=production`. This mode is **DEVELOPMENT ONLY**.

Google mode requires a real Auth.js secret and Google OAuth credentials, an approved Workspace domain, and an explicit Manager email allow-list. Domain membership alone is insufficient. Production deployment remains blocked until Google authentication is configured and manually verified.

### Server screenshot retention

`XUGAR_SCREENSHOT_STORAGE_ROOT` must be an absolute non-root directory outside `server/public`. Uploaded filenames are ignored. Run `npm run retention:cleanup` from `server`. The seven-day development default is not an approved production retention policy.

## Run the visible agent

```powershell
dotnet run --project .\src\Xugar.Endpoint.Agent\Xugar.Endpoint.Agent.csproj
```

Monitoring starts automatically after the window opens. The Stop and Start buttons are development controls. Closing the visible window stops both monitoring loops.

The committed settings in `src/Xugar.Endpoint.Agent/appsettings.json` are:

| Setting | Default |
|---|---:|
| Screenshot interval | 300 seconds |
| Process interval | 60 seconds |
| Retention | 24 hours |
| Data root | `%LOCALAPPDATA%\Xugar\EndpointMonitor\Data` |
| Server synchronization | Disabled |
| Server URL | `http://localhost:3000` (explicit loopback development only) |
| Heartbeat interval | 60 seconds |
| Policy refresh | 300 seconds |
| Policy cache maximum age | 900 seconds |
| Upload queue | 1,000 items / 100 MiB / 168 hours |

To exercise Phase 2B against the local development server, provide a disposable token that exactly matches the server's `XUGAR_ENROLLMENT_TOKEN`:

```powershell
$env:XUGAR_SERVER_SYNC_ENABLED = 'true'
$env:XUGAR_SERVER_BASE_URL = 'http://localhost:3000'
$env:XUGAR_ENROLLMENT_TOKEN = '<local-development-token-at-least-32-characters>'
dotnet run --project .\src\Xugar.Endpoint.Agent\Xugar.Endpoint.Agent.csproj
Remove-Item Env:XUGAR_SERVER_SYNC_ENABLED, Env:XUGAR_SERVER_BASE_URL, Env:XUGAR_ENROLLMENT_TOKEN
```

Arbitrary clear-text HTTP endpoints are rejected. Production endpoints must use HTTPS; TLS certificate validation is never disabled. The enrollment token is bootstrap-only and may be removed from the Agent environment after successful enrollment.

For a clearly labeled manual development test, override the screenshot interval without changing the committed default:

```powershell
$env:XUGAR_Monitoring__ScreenshotIntervalSeconds = '15'
dotnet run --project .\src\Xugar.Endpoint.Agent\Xugar.Endpoint.Agent.csproj
Remove-Item Env:XUGAR_Monitoring__ScreenshotIntervalSeconds
```

Configuration validation permits development intervals down to five seconds. Do not use a short interval as an approved production policy.

## Local data

The default layout is:

```text
%LOCALAPPDATA%\Xugar\EndpointMonitor\Data\
  yyyy-MM-dd\
    telemetry.jsonl
    process-current.csv
    process-events.csv
    process-summary.csv
    screenshots\
      yyyyMMddTHHmmssfffZ_monitor-1.png
  sync\
    installation-id
    device-credential.bin
    policy-cache.json
    queue\
      envelopes\
      payloads\
      corrupt\
```

The application refuses a filesystem volume root as its data root, confines generated paths to the configured root, does not follow reparse-point directories during cleanup, and tolerates files that cannot be deleted. Local development data and build artifacts are excluded by `.gitignore`.

### Process reports

`telemetry.jsonl` is the canonical raw source of truth. The CSV files are derived, human-readable reports and can be regenerated or discarded without changing the underlying telemetry meaning:

- `process-current.csv` is atomically replaced after each successful process snapshot and contains the latest observed process list.
- `process-events.csv` appends `START` and `STOP` transitions. The first snapshot after each agent startup establishes a baseline and does not generate a false flood of `START` rows.
- `process-summary.csv` is atomically replaced with a daily summary grouped by process name. Existing daily summary counts are continued when the agent restarts.

CSV reports use UTF-8 with a byte-order mark, CRLF line endings, headers, and RFC-compatible field escaping for Excel readability. Empty/inaccessible fields remain empty. The reports add a conservative path-derived category:

- `System`: executable is under the Windows directory;
- `Application`: an accessible executable path is outside the Windows directory;
- `Unknown`: the executable path is unavailable or cannot be safely interpreted.

This category is only a human-reporting aid and must not be used for blocking or other security decisions.

`SampleCount` means the number of periodic snapshots in which a process name was present; it is not exact application usage duration. A background process being present does not mean the employee was actively using it. `ForegroundSampleCount` counts snapshots where at least one instance was foreground and is only an approximate activity indicator.

Process events are sampling-based: a process that starts and stops entirely between snapshots may not appear. When executable paths are inaccessible, matching falls back to PID plus process name, so same-name PID reuse can be ambiguous.

If a CSV file is locked or cannot be written, canonical JSONL collection continues and a local operational warning is recorded. Retention cleanup treats CSV reports consistently with other files under the configured data root.

See [architecture](docs/ARCHITECTURE.md), [Phase 2B Agent synchronization](docs/PHASE2B_AGENT_SYNC.md), [privacy and data](docs/PRIVACY_AND_DATA.md), [Phase 2 API](docs/PHASE2_API.md), [security](docs/SECURITY.md), [operations](docs/OPERATIONS.md), [deployment](docs/DEPLOYMENT.md), [ActivTrak integration](docs/ACTIVTRAK_INTEGRATION.md), and the [manual test plan](docs/MANUAL_TEST_PLAN.md).

## Current limitations

- Desktop and lock/unlock behavior still requires testing in a real standard-user Windows session.
- GDI screenshots may not include protected video, hardware overlays, or DRM content.
- Publisher/signature metadata is not collected in Phase 1.
- Process categories and foreground samples are approximate reporting metadata, not security or exact usage evidence.
- The Service project is an inert future placeholder and is neither installed nor used by the agent.
- There is no tray icon, installer, code signing, autostart, ActivTrak webhook ingestion, remote control, or enforcement.
- The current server contract has one approved monitoring schedule shared by process and screenshot policy toggles; it does not yet express independent schedule-window sets for each telemetry type.
- DPAPI `CurrentUser` binds the device credential to the interactive Windows account. Moving synchronization ownership to a future Windows Service would require an explicit credential-migration design; the Service remains inert in Phase 2B.
- Manager authentication is not production-ready without real Google Workspace OAuth credentials and an explicit Manager allow-list.
