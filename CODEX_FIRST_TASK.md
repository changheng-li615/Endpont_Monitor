# Codex First Task — Phase 0/1 Repository Bootstrap

You are working on a new repository named **Xugar Endpoint Monitor**.

Read the repository root `AGENTS.md` and `docs/PROJECT_SPEC.md` completely before making changes.

## Objective

Bootstrap the repository and implement the smallest coherent **local Windows MVP foundation**. Do not implement future server or enforcement features.

## Environment assumptions

- Host OS: Windows 11.
- Shell: PowerShell.
- .NET SDK: .NET 10.
- Language: C#.
- UI: WPF.
- Tests: xUnit.
- This must be developed and run natively on Windows, not inside WSL or Docker.

## Required work

### A. Inspect and verify environment
Before changing files:
1. run `dotnet --info`;
2. run `git status`;
3. report the detected .NET SDK;
4. stop with a clear explanation if .NET 10 is unavailable.

Do not install software automatically unless explicitly asked.

### B. Create solution structure

Create:

```text
Xugar.EndpointMonitor.sln
src/Xugar.Endpoint.Core
src/Xugar.Endpoint.Agent
src/Xugar.Endpoint.Service
tests/Xugar.Endpoint.Tests
docs
```

Project roles:
- `Xugar.Endpoint.Core`: class library.
- `Xugar.Endpoint.Agent`: WPF application.
- `Xugar.Endpoint.Service`: .NET Worker Service placeholder for a later Windows Service.
- `Xugar.Endpoint.Tests`: xUnit project.

Add correct project references.

Create a .NET `.gitignore` if one is not already present.

### C. Add baseline engineering configuration

Enable:
- nullable reference types;
- implicit usings;
- deterministic builds where reasonable.

Do not turn all compiler warnings into errors yet unless the generated project is already clean enough for that to be useful.

Add a root README describing:
- purpose;
- current Phase 1 scope;
- how to build;
- how to test;
- how to run the WPF agent;
- explicit out-of-scope items.

### D. Implement Phase 1 foundation

In `Core`, create clear models/interfaces for:
- device/user context;
- process snapshot record;
- monitoring settings;
- screenshot metadata;
- operational event;
- `IProcessSnapshotProvider`;
- `IScreenshotCapture`;
- `ILocalTelemetryStore`.

Implement settings validation.

In `Agent`, implement:
- a visible main WPF window titled `Xugar Endpoint Monitor`;
- current monitoring status;
- configured screenshot interval;
- configured process interval;
- last screenshot timestamp;
- last process snapshot timestamp;
- local data directory;
- Start/Stop monitoring controls for development testing.

Implement a process snapshot provider using `System.Diagnostics.Process`.
One inaccessible process or inaccessible field must not fail the whole snapshot.
Do not collect command-line arguments.

Implement local JSONL telemetry writing.

Implement safe local data-path handling and retention cleanup.

Implement screenshot capture behind `IScreenshotCapture`.
Choose a stable Windows-compatible approach appropriate for a first .NET 10 WPF prototype.
Do not attempt to capture UAC secure desktop or bypass Windows protections.
Support multiple monitors if the chosen API makes that reasonably reliable; otherwise implement the primary screen first and document the limitation clearly rather than adding fragile code.

Implement two cancellable monitoring loops:
- process snapshot every 60 seconds by default;
- screenshot every 300 seconds by default.

Prevent overlapping iterations.
No network calls.
No process termination/suspension.

### E. Development configuration

Provide configuration with:
- `ScreenshotIntervalSeconds = 300`;
- `ProcessIntervalSeconds = 60`;
- `RetentionHours = 24`;
- local data root under a dedicated Xugar folder in the current user's LocalAppData.

It is acceptable to support a development override for a shorter screenshot interval, but the committed default remains five minutes.

### F. Tests

Add meaningful automated tests for logic that does not require a real interactive desktop, including at minimum:
- settings validation;
- filename generation;
- telemetry serialization;
- retention cutoff behavior;
- safe cleanup boundaries;
- cancellation where practical.

Do not add fake tests that merely assert `true`.

### G. Documentation

Create/update:
- `docs/ARCHITECTURE.md`;
- `docs/PRIVACY_AND_DATA.md`;
- `docs/MANUAL_TEST_PLAN.md`.

The privacy document must state that the MVP:
- is visible;
- does not keylog;
- does not record clipboard, microphone or webcam;
- does not collect credentials;
- does not capture process command-line arguments;
- does not attempt secure-desktop/UAC capture;
- stores data locally only in Phase 1.

### H. Verification

Run:

```powershell
dotnet restore
dotnet build Xugar.EndpointMonitor.sln -c Debug
dotnet test Xugar.EndpointMonitor.sln -c Debug --no-build
```

If the WPF app can be launched safely in the current environment, launch it and verify basic startup. If interactive desktop verification is unavailable, do not pretend it was tested; mark it as manual.

## Stop conditions

Stop rather than improvising if:
- .NET 10 is missing;
- the repository already contains conflicting architecture that requires a decision;
- a requested implementation would require hidden surveillance, credential collection, secure-desktop bypass, or automatic process enforcement;
- build/test failures cannot be resolved without materially expanding scope.

## Final response format

When done, report exactly:
1. repository/solution structure created;
2. files changed;
3. implemented behavior;
4. commands run and exact pass/fail results;
5. manual tests still required;
6. known limitations;
7. recommended next task.

Do not start Phase 2.
