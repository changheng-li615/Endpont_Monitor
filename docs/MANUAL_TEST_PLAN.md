# Manual Windows Test Plan

These checks require a real interactive Windows 11 desktop. Record the tester, device/build, date, configuration override, expected result, actual result, and pass/fail for each check. Do not treat the automated build as evidence that desktop capture was exercised.

## Preparation

1. Use a company-authorized test laptop and a standard (non-administrator) Windows account.
2. Run `dotnet build Xugar.EndpointMonitor.sln -c Debug` and `dotnet test Xugar.EndpointMonitor.sln -c Debug --no-build`.
3. Confirm `%LOCALAPPDATA%\Xugar\EndpointMonitor\Data` contains no data needed by another test. Move any needed test evidence before exercising retention.
4. For timing tests only, set `$env:XUGAR_Monitoring__ScreenshotIntervalSeconds = '15'`. Keep the committed default at 300 seconds and remove the environment variable after testing.

## Visibility and controls

1. Run `dotnet run --project .\src\Xugar.Endpoint.Agent\Xugar.Endpoint.Agent.csproj`.
2. Verify a normal window titled **Xugar Endpoint Monitor** is visible and identifies the prototype as local-only.
3. Verify it shows status, 300-second screenshot interval (or the clearly identified override), 60-second process interval, last screenshot, last process snapshot, and local data directory.
4. Verify monitoring starts automatically.
5. Select **Stop**. Wait longer than both configured intervals and verify timestamps/files do not advance.
6. Select **Start** and verify capture resumes.
7. Close the window and verify the agent process exits; confirm no service or hidden background process remains.

## Local telemetry and process resilience

1. Open and close several ordinary applications, including Notepad.
2. Wait for a process snapshot and inspect the current date's `telemetry.jsonl`.
3. Verify each line is valid JSON and process snapshots reflect opened/closed applications.
4. Verify machine/user, process name, PID, accessible path/version, working set, and foreground status fields are present as designed.
5. Verify inaccessible fields are omitted/null and a protected or rapidly exiting process does not stop later snapshots.
6. Search telemetry for `commandLine`, known test passwords, tokens, and clipboard content; verify none was collected.

## Screenshot behavior

1. On an unlocked normal desktop, wait for capture and verify a timestamped PNG exists under the current date's `screenshots` directory.
2. Open the PNG and verify it corresponds to the expected monitor and timestamp. Verify no pixel data appears inside JSONL.
3. If a second monitor is available, connect it before the next iteration. Verify one correctly bounded PNG is created per monitor with `monitor-1`, `monitor-2`, and so on.
4. Check mixed DPI/scaling and a monitor positioned left or above the primary display if the test hardware supports it.
5. Lock Windows and remain locked for at least two short test intervals. Unlock, then verify no screenshot timestamp/file was produced while the normal desktop was unavailable and monitoring subsequently resumes.
6. If organizational policy permits a UAC prompt test, trigger a normal signed Windows elevation prompt without entering sensitive data. Verify the agent does not capture or bypass the secure desktop. Cancel the prompt and verify later normal-desktop capture resumes.
7. Note any black or missing protected-video/hardware-overlay content as an expected GDI limitation.

## Retention and path safety

1. Copy disposable test files into a dated subdirectory beneath the configured Xugar data root.
2. Set one test file's last-write time older than the configured cutoff and keep another newer than the cutoff.
3. Start monitoring and verify the expired file is deleted while the newer file remains.
4. Keep a disposable expired file open with a deny-delete share mode if practical. Verify cleanup records a failure and monitoring continues.
5. Confirm no file outside the configured Xugar data root changed.
6. Attempt to configure a volume root such as `C:\`; verify startup rejects the setting.

## Cleanup

1. Close the agent.
2. Remove the temporary environment override: `Remove-Item Env:XUGAR_Monitoring__ScreenshotIntervalSeconds -ErrorAction SilentlyContinue`.
3. Retain test evidence only according to the approved prototype policy; screenshots may contain sensitive information.

