# Xugar Endpoint Monitor

Xugar Endpoint Monitor is a transparent, company-authorized Windows 11 endpoint-monitoring prototype for company-owned laptops. Phase 1 runs as a visible WPF application in the signed-in employee session and stores its telemetry only on the local device.

## Phase 1 capabilities

- Visible WPF status window with Start and Stop controls.
- Process snapshots every 60 seconds by default.
- PNG screenshots every 300 seconds (five minutes) by default.
- One screenshot per detected monitor when the normal interactive desktop is available.
- Local JSONL process, screenshot-metadata, operational, and retention records.
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
    screenshots\
      yyyyMMddTHHmmssfffZ_monitor-1.png
```

The application refuses a filesystem volume root as its data root, confines generated paths to the configured root, does not follow reparse-point directories during cleanup, and tolerates files that cannot be deleted. Local development data and build artifacts are excluded by `.gitignore`.

See [architecture](docs/ARCHITECTURE.md), [privacy and data](docs/PRIVACY_AND_DATA.md), and the [manual Windows test plan](docs/MANUAL_TEST_PLAN.md).

## Current limitations

- Desktop and lock/unlock behavior still requires testing in a real standard-user Windows session.
- GDI screenshots may not include protected video, hardware overlays, or DRM content.
- Publisher/signature metadata is not collected in Phase 1.
- The Service project is an inert future placeholder and is neither installed nor used by the agent.
- There is no tray icon, installer, code signing, autostart, backend, upload queue, dashboard, or enforcement.

