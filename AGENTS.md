# Xugar Endpoint Monitor — AGENTS.md

## Project purpose

Build a transparent, company-authorized Windows endpoint monitoring application for company-owned Windows 11 laptops.

The product may:
- show a visible tray/status indicator;
- capture periodic desktop screenshots while a normal employee session is active and unlocked;
- collect running-application/process metadata;
- record local health and audit logs;
- later upload encrypted telemetry to an approved Xugar backend;
- later integrate with approved Windows application-control policy.

The product must NOT:
- operate as hidden or deceptive surveillance;
- implement keylogging;
- collect typed passwords or browser credentials;
- collect clipboard history;
- activate microphone or webcam recording;
- attempt to capture the Windows secure desktop or bypass UAC;
- disable security products;
- automatically suspend/kill unknown processes in the MVP;
- upload telemetry in Phase 1;
- embed passwords, API secrets, manager credentials, or tokens in source code.

## Current delivery stage

Phase 0/1 only: local Windows prototype.

Implement:
1. solution/repository structure;
2. visible WPF user agent;
3. process snapshot collection;
4. periodic screenshots;
5. local JSON/JSONL logging;
6. retention cleanup;
7. tests and documentation.

Do NOT implement backend upload, remote management, automatic enforcement, Windows Service installation, or stealth/autostart persistence yet. A Worker project may be scaffolded as a placeholder, but Phase 1 must run without installing a service.

## Technology baseline

- Windows 11
- C# 14
- .NET 10
- WPF for the visible user-session agent
- .NET Worker Service placeholder for later background/service responsibilities
- xUnit for tests
- Microsoft.Extensions.Hosting / Configuration / Logging where appropriate

Do not introduce Electron, Python, Node.js, Docker, WSL, a database, or cloud dependencies into the Windows Agent MVP unless explicitly requested.

## Architecture boundaries

Recommended projects:
- `src/Xugar.Endpoint.Core` — domain models, interfaces, settings contracts; no UI and minimal platform dependencies.
- `src/Xugar.Endpoint.Agent` — WPF app, tray/status UI, timers/orchestration for the logged-in user session, screenshot provider.
- `src/Xugar.Endpoint.Service` — scaffold/placeholder for a future Windows Service; must not own desktop screenshot capture.
- `tests/Xugar.Endpoint.Tests` — unit tests for platform-independent logic.
- `docs` — architecture, privacy, test plan, operational notes.

Keep screenshot capture behind an interface such as `IScreenshotCapture`.
Keep process collection behind an interface such as `IProcessSnapshotProvider`.
Keep persistence behind an interface such as `ILocalTelemetryStore`.
Keep time behind an injectable abstraction where useful for testing.

## Data-minimization rules

For Phase 1 process telemetry collect only what is needed:
- timestamp;
- device/host name;
- current Windows user;
- process name;
- PID;
- executable path when accessible;
- product/file version when accessible;
- publisher/signature information only if implemented safely;
- working set memory;
- foreground status if available.

Do not collect process command-line arguments in Phase 1 because they may contain secrets.

For screenshots:
- default interval: 300 seconds;
- only capture an interactive, unlocked user session;
- do not attempt secure-desktop/UAC capture;
- support multiple monitors if reasonably achievable;
- use JPEG or PNG initially;
- configurable local retention, default 24 hours for the prototype;
- store under a dedicated development data directory, not alongside source code;
- add the data directory to `.gitignore`.

## Configuration

Use normal .NET configuration. Development settings should include:
- screenshot interval seconds (default 300);
- process snapshot interval seconds (default 60);
- output directory;
- retention hours (default 24);
- logging level.

Do not hard-code production policy values into implementation classes.

## Quality and safety rules

Before modifying code:
1. inspect the repository;
2. read applicable `AGENTS.md`;
3. state the smallest coherent implementation plan;
4. preserve existing passing behavior.

For each change:
- prefer small reviewable commits/patches;
- add tests for logic that can be tested without a real desktop;
- handle AccessDenied/Win32 exceptions without crashing;
- use CancellationToken for background loops;
- dispose bitmaps/streams/process resources correctly;
- avoid unbounded queues and unbounded disk growth;
- never log passwords, auth tokens, or screenshot pixel data as text.

## Verification

A Phase 1 change is not complete until relevant commands pass:

```powershell
dotnet restore
dotnet build Xugar.EndpointMonitor.sln -c Debug
dotnet test Xugar.EndpointMonitor.sln -c Debug --no-build
```

For UI/system behavior that cannot be validated headlessly, provide a concise manual test checklist and clearly mark it as manual.

## MVP acceptance criteria

- Solution builds on Windows with .NET 10.
- WPF agent launches as a normal user.
- Agent visibly identifies itself as Xugar Endpoint Monitor.
- A user can see current monitoring status and last capture/snapshot time.
- Screenshot scheduler defaults to every 5 minutes.
- Process scheduler defaults to every 60 seconds.
- Local telemetry is written successfully.
- Failure to inspect one process does not crash the agent.
- Old prototype screenshots/logs are removed according to configured retention.
- No backend/network upload occurs.
- No process is terminated or suspended.
- No keylogging, clipboard, mic, webcam, credential collection, or secure-desktop bypass exists.
- Automated tests pass.
- README explains setup, run, test, data location, and current limitations.

## Working style for Codex

Do not implement several future phases at once.
If a requirement is ambiguous, choose the safest minimal interpretation and document the assumption.
When you finish a task, report:
1. files changed;
2. behavior added;
3. commands/tests run and exact result;
4. manual checks still required;
5. risks or follow-up items.
