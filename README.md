# Xugar Endpoint Monitor

Xugar Endpoint Monitor is a transparent, company-authorized Windows 11 endpoint-monitoring prototype for company-owned laptops. Phase 1 runs as a visible WPF application in the signed-in employee session and stores its telemetry only on the local device.

## Phase 1 capabilities

- Visible WPF status window with Start and Stop controls.
- Process snapshots every 60 seconds by default.
- PNG screenshots every 300 seconds (five minutes) by default.
- One screenshot per detected monitor when the normal interactive desktop is available.
- Local JSONL process, screenshot-metadata, operational, and retention records.
- Derived human-readable daily process CSV reports.
- Local file retention of 24 hours by default.
- Resilient handling of inaccessible or short-lived processes.

The prototype makes no network calls. It does not upload data, manage devices remotely, enforce application policy, install a Windows Service, start automatically, hide itself, capture command lines, keylog, capture the clipboard, collect credentials, activate a microphone or webcam, or bypass UAC/secure desktop.

## Prerequisites

- Windows 11 x64
- .NET 10 SDK

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

See [architecture](docs/ARCHITECTURE.md), [privacy and data](docs/PRIVACY_AND_DATA.md), and the [manual Windows test plan](docs/MANUAL_TEST_PLAN.md).

## Current limitations

- Desktop and lock/unlock behavior still requires testing in a real standard-user Windows session.
- GDI screenshots may not include protected video, hardware overlays, or DRM content.
- Publisher/signature metadata is not collected in Phase 1.
- Process categories and foreground samples are approximate reporting metadata, not security or exact usage evidence.
- The Service project is an inert future placeholder and is neither installed nor used by the agent.
- There is no tray icon, installer, code signing, autostart, backend, upload queue, dashboard, or enforcement.
