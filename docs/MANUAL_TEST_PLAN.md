# Manual Test Plan

These checks require a real interactive Windows 11 desktop. Record the tester, device/build, date, configuration override, expected result, actual result, and pass/fail for each check. Do not treat the automated build as evidence that desktop capture was exercised.

Use this evidence format for each executed check:

| Test ID | Setup | Steps | Expected | Actual | PASS/FAIL | Evidence | Notes |
|---|---|---|---|---|---|---|---|
| Example | Authorized test environment | Numbered actions | Observable result | Tester records | Tester records | Screenshot/log reference | No secrets |

## Preparation

1. Use a company-authorized test laptop and a standard (non-administrator) Windows account.
2. Run `dotnet build Xugar.EndpointMonitor.sln -c Debug` and `dotnet test Xugar.EndpointMonitor.sln -c Debug --no-build`.
3. Confirm `%LOCALAPPDATA%\Xugar\EndpointMonitor\Data` contains no data needed by another test. Move any needed test evidence before exercising retention.
4. For timing tests only, set `$env:XUGAR_Monitoring__ScreenshotIntervalSeconds = '15'`. Keep the committed default at 300 seconds and remove the environment variable after testing.

## Visibility and controls

1. Run `dotnet run --project .\src\Xugar.Endpoint.Agent\Xugar.Endpoint.Agent.csproj`.
2. Verify a normal window titled **Xugar Endpoint Monitor** is visible and identifies the prototype as local-only.
3. Verify it shows status, 300-second screenshot interval (or the clearly identified override), 60-second process interval, last screenshot, last process snapshot, and local data directory.
4. Select **Open Data Folder** and verify Windows Explorer opens the configured Xugar data root.
5. Verify monitoring starts automatically.
6. Select **Stop**. Wait longer than both configured intervals and verify timestamps/files do not advance.
7. Select **Start** and verify capture resumes.
8. Close the window and verify the agent process exits; confirm no service or hidden background process remains.

## Local telemetry and process resilience

1. Open and close several ordinary applications, including Notepad.
2. Wait for a process snapshot and inspect the current date's `telemetry.jsonl`.
3. Verify each line is valid JSON and process snapshots reflect opened/closed applications.
4. Verify machine/user, process name, PID, accessible path/version, working set, and foreground status fields are present as designed.
5. Verify inaccessible fields are omitted/null and a protected or rapidly exiting process does not stop later snapshots.
6. Search telemetry for `commandLine`, known test passwords, tokens, and clipboard content; verify none was collected.

## Human-readable process reports

1. Verify `telemetry.jsonl` remains present and valid; treat it as the canonical raw record. Treat all CSV files as derived reports.
2. After a successful process snapshot, verify `process-current.csv`, `process-events.csv`, and `process-summary.csv` exist in the same daily directory as `telemetry.jsonl`.
3. Open each CSV in Excel and a text editor. Verify headers, UTF-8 characters, commas/quotes in fields, and empty inaccessible fields display correctly.
4. Verify `process-current.csv` contains only the latest snapshot and includes category, PID, accessible path/version, working-set MB, and foreground state.
5. Start the agent with several applications already running. Verify the first snapshot creates only the `process-events.csv` header and does not produce hundreds of false `START` events.
6. Start Notepad, wait for a snapshot, close Notepad, and wait for another snapshot. Verify one corresponding `START` and `STOP` transition, allowing for PID changes caused by Windows application behavior.
7. Restart the agent. Verify the first post-restart snapshot is again an event baseline and the existing daily summary continues rather than resetting its counts.
8. Verify `process-summary.csv` groups primarily by process name, updates first/last seen times, counts one presence sample per process name per snapshot, records peak working set, and increments foreground samples only when observed foreground.
9. Confirm with reviewers that `SampleCount` is not interpreted as exact application usage duration, background process presence is not interpreted as employee activity, and `ForegroundSampleCount` is treated only as an approximate indicator.
10. Review several `Application`, `System`, and `Unknown` categories. Confirm they are sensible human labels and are not used for blocking, enforcement, or security decisions.
11. Keep `process-current.csv` open in an application that prevents replacement, then wait for another snapshot. Verify JSONL continues to update and a local `processReport` warning is recorded even though the derived CSV update fails.

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
4. Include disposable CSV files in the retention test and verify they follow the same cutoff behavior as JSONL and screenshots.
5. Keep a disposable expired file open with a deny-delete share mode if practical. Verify cleanup records a failure and monitoring continues.
6. Confirm no file outside the configured Xugar data root changed.
7. Attempt to configure a volume root such as `C:\`; verify startup rejects the setting.

## Cleanup

1. Close the agent.
2. Remove the temporary environment override: `Remove-Item Env:XUGAR_Monitoring__ScreenshotIntervalSeconds -ErrorAction SilentlyContinue`.
3. Retain test evidence only according to the approved prototype policy; screenshots may contain sensitive information.

## Phase 2A central server manual checks

| Test ID | Setup | Steps | Expected | Actual | PASS/FAIL | Evidence | Notes |
|---|---|---|---|---|---|---|---|
| P2A-DB-01 | Docker Desktop running | Start Compose and inspect `docker compose -f docker-compose.dev.yml ps` | PostgreSQL 18 is healthy on loopback port 55432 | | | | Do not delete volume |
| P2A-DB-02 | Configured `DATABASE_URL` | Deploy migrations, restart container, check status | Both tracked migrations remain applied; data survives restart | | | | No `db push` |
| P2A-AUTH-01 | No Manager auth variables | Start server and open `/admin` | Manager data is inaccessible | | | | Safe default |
| P2A-AUTH-02 | Non-production explicit development flags | Open overview, device list, and detail | Dashboard is locally accessible | | | | DEVELOPMENT ONLY |
| P2A-AUTH-03 | Real Google test OAuth app and allow-list | Sign in as allowed Manager and non-allowed user | Allowed Manager succeeds; other user is denied | | | | Required before staging |
| P2A-API-01 | Synthetic `@example.invalid` device | Enroll, call heartbeat and policy | Secret returned once; policy defaults disabled | | | | Never record secret |
| P2A-API-02 | Enrolled synthetic device | Replace processes twice; post START/STOP | Removed current row disappears; history remains | | | | No command lines |
| P2A-SHOT-01 | Disposable synthetic PNG/JPEG | Upload and view through Manager route | Private generated files/hashes; unauthenticated read denied | | | | No real screenshots |
| P2A-RET-01 | Disposable expired screenshot | Run `npm run retention:cleanup` | Only confined expired file/metadata removed; audit written | | | | Retention approval required |
| P2A-PRIV-01 | Synthetic dashboard data | Review all labels | Sources are distinct; no employee-activity inference | | | | No productivity score |
