# Xugar Endpoint Monitor — Phase 0/1 Technical Specification

## 1. Goal

Create a local, transparent Windows 11 endpoint-monitoring prototype for company-owned laptops.

The prototype proves three capabilities:
1. a visible employee-session agent can run reliably;
2. it can capture periodic desktop screenshots;
3. it can record application/process activity locally.

This phase intentionally does not provide remote monitoring or application enforcement.

## 2. Scope

### In scope
- Windows 11 x64 development target.
- .NET 10 / C#.
- WPF visible agent.
- Periodic screenshots, default every 300 seconds.
- Running-process snapshots, default every 60 seconds.
- Host/user identification.
- Local structured telemetry.
- Configurable prototype retention.
- Resilient error handling.
- Unit tests for non-UI logic.
- Manual test plan for Windows desktop behavior.
- Documentation.

### Out of scope
- Cloud/server upload.
- PostgreSQL.
- Manager web dashboard.
- Windows Service installation.
- Remote commands.
- Process kill/suspend/block.
- AppLocker/App Control policy deployment.
- Keylogging.
- Clipboard capture.
- Microphone/webcam.
- Browser-history/password extraction.
- Hidden monitoring.
- UAC or secure-desktop bypass.
- Production MSI/code signing.

## 3. Proposed solution layout

```text
Xugar.EndpointMonitor.sln
├─ AGENTS.md
├─ README.md
├─ docs/
│  ├─ ARCHITECTURE.md
│  ├─ PRIVACY_AND_DATA.md
│  └─ MANUAL_TEST_PLAN.md
├─ src/
│  ├─ Xugar.Endpoint.Core/
│  ├─ Xugar.Endpoint.Agent/
│  └─ Xugar.Endpoint.Service/
└─ tests/
   └─ Xugar.Endpoint.Tests/
```

## 4. Core components

### 4.1 DeviceContext
Provides:
- machine name;
- current user;
- operating-system version;
- app version;
- current timestamp.

### 4.2 ProcessSnapshotProvider
Enumerates processes with `System.Diagnostics.Process`.

Each record should attempt to include:
- process name;
- PID;
- executable path if permitted;
- file/product version if permitted;
- working-set memory;
- whether it is the foreground application if implemented.

Access-denied fields are nullable and must not fail the full snapshot.

Do not collect command-line arguments.

### 4.3 ScreenshotCapture
Interface-driven Windows screenshot component.

Requirements:
- runs only from the logged-in interactive agent;
- capture normal desktop content;
- do not attempt secure-desktop/UAC capture;
- support multiple displays where practical;
- file names must contain timestamp and monitor index;
- no image is committed to Git.

Implementation should be isolated so the capture technology can be replaced later.

### 4.4 MonitoringScheduler
Runs two independent cancellable loops:
- screenshot loop: 300 seconds by default;
- process loop: 60 seconds by default.

The app must not create overlapping captures if one operation is slow.
Use cancellation and exception isolation.

### 4.5 LocalTelemetryStore
Stores structured process snapshots and operational events.

Preferred prototype format:
- JSONL for events/process snapshots;
- screenshot files in dated directories.

Example:
```text
data/
  2026-08-25/
    telemetry.jsonl
    screenshots/
      20260825T114500_monitor-1.jpg
```

The actual root directory should be configurable and excluded from Git.

### 4.6 RetentionCleanup
Default prototype retention: 24 hours.

Cleanup must:
- stay inside the configured Xugar data root;
- never recursively delete arbitrary parent directories;
- tolerate files in use;
- log cleanup result.

### 4.7 Visible status UI
The WPF agent should show:
- product name: Xugar Endpoint Monitor;
- monitoring status;
- current screenshot interval;
- current process interval;
- last screenshot time;
- last process snapshot time;
- local data directory;
- Stop/Start monitoring control for development testing only.

A tray icon may be added if simple and stable, but the MVP must not be hidden.

## 5. Configuration

Example development configuration:
```json
{
  "Monitoring": {
    "ScreenshotIntervalSeconds": 300,
    "ProcessIntervalSeconds": 60,
    "RetentionHours": 24
  },
  "Storage": {
    "RootPath": "%LOCALAPPDATA%\\Xugar\\EndpointMonitor\\Data"
  }
}
```

Support a development override that can temporarily use a shorter screenshot interval for manual testing, without changing the production/default value of 300 seconds.

## 6. Logging

Use structured logging.

Important events:
- app start/stop;
- monitoring start/stop;
- screenshot success/failure;
- process snapshot success/failure/count;
- retention cleanup result;
- inaccessible process metadata.

Never log:
- passwords;
- auth tokens;
- browser credentials;
- screenshot contents;
- process command lines.

## 7. Tests

Automated tests should cover:
- settings validation;
- telemetry serialization;
- file naming;
- safe storage-path construction;
- retention cutoff calculation;
- cleanup does not delete files newer than cutoff;
- cleanup stays within allowed root;
- scheduler cancellation;
- process mapping with inaccessible nullable fields where testable.

Manual tests:
1. launch app as standard Windows user;
2. verify visible identity/status;
3. set a short development capture interval;
4. confirm screenshot files appear;
5. connect a second display if available and verify behavior;
6. lock Windows and confirm the app does not bypass/record secure desktop;
7. unlock and confirm monitoring resumes safely;
8. open/close normal apps and confirm process snapshots reflect changes;
9. verify denied process metadata does not crash the app;
10. verify retention deletes only expired prototype files.

## 8. Phase 1 completion gate

Do not proceed to backend/server work until:
- build is clean;
- automated tests pass;
- manual Windows checklist is completed;
- disk-use behavior is understood;
- privacy/data fields are reviewed with management;
- screenshot policy and retention have explicit management approval.

## 9. Future phases

Phase 2:
- device registration;
- authenticated HTTPS API;
- PostgreSQL metadata;
- object storage for images;
- offline upload queue;
- manager dashboard.

Phase 3:
- production Windows Service for health/telemetry responsibilities;
- signed installer;
- code signing;
- auto-update;
- audit trail.

Phase 4:
- application policy in audit mode;
- publisher/hash/path classification;
- AppLocker/App Control integration where appropriate.

Phase 5:
- enforcement only after audit results and approved allow/block policy.
