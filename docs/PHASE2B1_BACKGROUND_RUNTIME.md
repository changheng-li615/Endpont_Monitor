# Phase 2B.1 Windows Background Runtime

## Scope

Phase 2B.1 wraps the existing Phase 2B synchronization coordinator in a practical, transparent Windows user-session lifecycle. It does not add a Windows Service, watchdog, hidden monitoring, enforcement, remote commands, or ActivTrak integration.

## Launch and window lifecycle

| Action | Result |
|---|---|
| Normal executable launch | Starts the runtime once, shows the existing status window, and shows the tray icon |
| Executable launch with `--startup` or `--background` | Starts the same runtime once without initially showing the window; the tray icon remains visible |
| Window X | Cancels the close and hides the window; monitoring and synchronization continue |
| Window minimize | Uses normal WPF minimize behavior |
| Tray icon click/double-click or **Open Xugar Monitor** | Shows and activates the existing window |
| Tray **Exit** | Cancels monitoring/network loops, preserves the durable queue, disposes the tray/host, and exits |

WPF uses `ShutdownMode.OnExplicitShutdown`; window visibility does not own or start the monitoring coordinator. The existing Start/Stop buttons control the coordinator but cannot bypass central policy. A valid central policy and schedule remain required for centrally governed collection.

## Single instance

The Agent creates a named per-user/session semaphore. Its name contains a short SHA-256-derived user-scope identifier rather than the Windows username. A second process cannot acquire that semaphore and therefore never creates another monitoring, screenshot, heartbeat, policy, or upload loop.

A primary instance also owns a named auto-reset activation event. A second normal launch signals that event and exits, causing the primary process to restore its existing window. A duplicate `--startup` launch exits without disturbing the primary runtime.

## Current-user sign-in startup

Startup registration uses:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  XugarEndpointMonitor = "<absolute stable path>\Xugar.Endpoint.Agent.exe" --startup
```

The registration is idempotent and uses the current user's registry hive. It does not require elevation, store credentials, create a scheduled task, run as SYSTEM, or move screenshot capture out of the interactive session. The status-window checkbox and the `--configure --enable-startup` command update the same value. Explicit tray Exit does not self-respawn the process; enabled startup takes effect at the next Windows sign-in.

Use a stable authorized pilot path such as `C:\Program Files\Xugar\EndpointMonitor`. A Debug build path is acceptable only for a labelled development test. The Agent does not copy itself into the stable path or request installation elevation.

## Persistent non-secret configuration

`%LOCALAPPDATA%\Xugar\EndpointMonitor\config.json` contains the versioned non-secret synchronization settings and startup preference. Writes use a same-directory atomic replacement. Unknown members and malformed or invalid content are rejected; the file is renamed to `config.corrupt.<timestamp>.<id>.json`, synchronization returns to safe disabled defaults, and a sanitized warning is logged.

The persistent schema deliberately cannot represent an enrollment token, device secret, authorization header, or Manager/server credential. Effective configuration precedence, highest first, is:

1. explicit .NET command-line configuration arguments;
2. `XUGAR_` environment keys and documented aliases;
3. persistent `config.json`;
4. committed `appsettings.json` defaults.

One-time pilot configuration example:

```powershell
& 'C:\Program Files\Xugar\EndpointMonitor\Xugar.Endpoint.Agent.exe' `
  --configure `
  --enable-sync `
  --server-url 'https://monitor.example.com' `
  --enable-startup
```

Use `--configure --disable-sync` or `--configure --disable-startup` for the corresponding idempotent changes. HTTP endpoints remain limited to explicitly allowed loopback development; non-loopback deployments require HTTPS.

## Enrollment and restart

First enrollment receives `XUGAR_ENROLLMENT_TOKEN` only in the environment of that launch. The server-issued device credential remains in `Data\sync\device-credential.bin`, protected by Windows DPAPI `CurrentUser`; the installation GUID remains in `Data\sync\installation-id`.

On later starts, `DeviceEnrollmentService` reads and validates the protected credential before checking for a bootstrap token. The same Windows user can therefore reconnect after an Agent or Windows restart using the existing installation/device identity, persistent sync URL/enabled setting, and DPAPI credential. No token is written into JSON, registry startup commands, logs, telemetry, or UI. If neither credential nor token exists, the visible status reports a configuration/enrollment problem and existing bounded retry behavior prevents a tight enrollment loop.

## Publish and pilot placement

From the repository root:

```powershell
.\scripts\publish-agent.ps1
```

The default output is a self-contained `win-x64` directory at `artifacts\Xugar.Endpoint.Agent`. `-FrameworkDependent` is available only when the .NET 10 Windows Desktop Runtime is an approved target prerequisite. Copy the entire output directory to the stable pilot location through an authorized deployment step, then run the one-time configuration command from that stable executable. The script does not install, elevate, register startup automatically, create an MSI, or store secrets.

## Security and operational limits

- A visible notification-area icon is present for the lifetime of the running Agent.
- The process remains identifiable as Xugar Endpoint Monitor and the status window is always reachable from the tray.
- Screenshot capture remains on the normal interactive `Default` desktop and retains existing lock/UAC secure-desktop protections.
- DPAPI `CurrentUser` credentials cannot be moved to another Windows account by design.
- There is no watchdog or guaranteed runtime after an employee deliberately selects Exit.
- HKCU startup depends on the stable executable remaining at the registered path.
- MSI packaging, code signing, fleet deployment, service ownership, and ActivTrak integration remain outside Phase 2B.1.

The real X-02 Windows-restart/GCPW acceptance sequence and offline queue regression are specified in [MANUAL_TEST_PLAN.md](MANUAL_TEST_PLAN.md).
