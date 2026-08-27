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
2. Verify a normal window titled **Xugar Endpoint Monitor** is visible and identifies optional server synchronization without claiming hidden operation.
3. Verify it shows local status, 300-second screenshot interval (or the clearly identified override), 60-second process interval, last screenshot, last process snapshot, local data directory, and compact synchronization status.
4. Select **Open Data Folder** and verify Windows Explorer opens the configured Xugar data root.
5. Verify monitoring starts automatically.
6. Select **Stop**. Wait longer than both configured intervals and verify timestamps/files do not advance.
7. Select **Start** and verify capture resumes.
8. Close the window and verify the window hides while the Agent process and visible tray icon remain. Reopen the same window from the tray, then use tray **Exit** and verify the process terminates.

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

1. Exit the Agent from the tray menu. Closing only the WPF window intentionally leaves the transparent background runtime active.
2. Remove the temporary environment override: `Remove-Item Env:XUGAR_Monitoring__ScreenshotIntervalSeconds -ErrorAction SilentlyContinue`.
3. Retain test evidence only according to the approved prototype policy; screenshots may contain sensitive information.

## Phase 2B.1 Windows background runtime and restart acceptance

Run these checks against the complete published `win-x64` directory copied to an approved stable pilot path. For development-only checks, a Debug executable path may be registered temporarily, but that is not pilot deployment evidence. Use the normal GCPW standard-user account for runtime tests. Record Task Manager process counts, tray/window observations, sanitized local timestamps, and central device/heartbeat identifiers; never record an enrollment token or device-secret content.

| Test ID | Setup | Steps | Expected | Actual | PASS/FAIL | Evidence | Notes |
|---|---|---|---|---|---|---|---|
| P2B1-A Normal launch | Agent not running | Double-click `Xugar.Endpoint.Agent.exe` | One Agent process starts; status window and visible Xugar tray icon appear; monitoring starts once | | | | Window is expected on manual launch |
| P2B1-B Close window | P2B1-A running and synchronized | Click the WPF window X; observe Task Manager, tray, central heartbeat/process/screenshot timestamps | Window hides; same Agent process/tray remain; heartbeat, process sync, queue worker, and policy-eligible screenshots continue | | | | Close is not Exit |
| P2B1-C Reopen | Window hidden after P2B1-B | Select tray **Open Xugar Monitor**, then single-click and double-click the icon | Existing window returns to foreground; same process, enrollment, queue, and advancing timestamps remain; no runtime is recreated | | | | Either icon click must open |
| P2B1-D Duplicate launch | Agent already running normally or via `--startup` | Double-click the same executable; repeat once | Only one Agent process/runtime remains and the existing window opens; no duplicate capture/heartbeat loops | | | | Check process count and cadence |
| P2B1-E Explicit Exit | Agent running with window shown or hidden | Select tray **Exit**; inspect process, tray, local files, and pending queue | Loops cancel cleanly, safe writes finish, queue remains valid, tray disappears, and Agent process terminates | | | | No immediate watchdog respawn |
| P2B1-F Auto-start | Published Agent configured with `--enable-startup` | Confirm HKCU Run value, restart Windows, sign in through GCPW | Agent starts once with `--startup`; no WPF window appears; visible tray icon appears | | | | Run value must use stable quoted path |
| P2B1-G No PowerShell dependency | Persistent sync URL/enabled setting and prior enrollment exist | Close all PowerShell windows before P2B1-F; after sign-in run no setup commands | Sync remains enabled, server URL is known, same central device connects, and heartbeat resumes | | | | Inspect `config.json` only for non-secrets |
| P2B1-H No enrollment-token dependency | Successful enrollment exists; `XUGAR_ENROLLMENT_TOKEN` absent | Exit and restart Agent, then restart Windows | Existing DPAPI credential and installation ID are reused; no re-enrollment or second device row occurs | | | | Do not open/decrypt credential as evidence |
| P2B1-I Central policy | Agent running only in tray | Change/assign approved policy and wait for refresh | Policy status/intervals update; collection follows toggles/schedule; tray/background mode does not bypass policy | | | | Heartbeat may remain active |
| P2B1-J Server offline | Agent synchronized and hidden to tray | Stop server, observe local telemetry/queue, restart server, wait through backoff | Agent/tray remain; local canonical records continue; bounded eligible queue grows, reconnects, and drains without duplicates | | | | Opening/closing UI has no effect |
| P2B1-K Standard user | GCPW standard-user session | Repeat A-J runtime actions without elevation | Normal launch, HKCU startup, capture, sync, tray interaction, and Exit require no administrator password or UAC | | | | Authorized installation may be separate |

### Required X-02 restart evidence

The Phase 2B.1 acceptance gate is a real restart, not merely an Agent process restart:

1. Confirm X-02 is enrolled and synchronized, persistent configuration enables sync and points to the approved server, and HKCU startup points to the stable published executable with `--startup`.
2. Remove the bootstrap-token environment variable, close all PowerShell windows, and record the existing installation ID and central device ID without recording secret material.
3. Restart Windows and sign in through GCPW as the same standard user. Do not run PowerShell setup or enter administrator credentials.
4. Verify no main window opens, a visible Xugar tray icon appears, and exactly one Agent process runs.
5. Verify the same installation/device IDs remain, heartbeat and policy refresh resume, process monitoring resumes, and screenshots resume only when central policy and the normal unlocked desktop allow them.
6. Verify no second X-02 device record was created. Record PASS/FAIL and sanitized timestamps/IDs in the evidence table.

## Phase 2A central server manual checks

| Test ID | Setup | Steps | Expected | Actual | PASS/FAIL | Evidence | Notes |
|---|---|---|---|---|---|---|---|
| P2A-DB-01 | Docker Desktop running | Start Compose and inspect `docker compose -f docker-compose.dev.yml ps` | PostgreSQL 18 is healthy on loopback port 55432 | | | | Do not delete volume |
| P2A-DB-02 | Configured `DATABASE_URL` | Deploy migrations, restart container, check status | All tracked migrations remain applied; data survives restart | | | | No `db push` |
| P2A-AUTH-01 | No Manager auth variables | Start server and open `/admin` | Manager data is inaccessible | | | | Safe default |
| P2A-AUTH-02 | Non-production explicit development flags | Open overview, device list, and detail | Dashboard is locally accessible | | | | DEVELOPMENT ONLY |
| P2A-AUTH-03 | Real Google test OAuth app and allow-list | Sign in as allowed Manager and non-allowed user | Allowed Manager succeeds; other user is denied | | | | Required before staging |
| P2A-API-01 | Synthetic `@example.invalid` device | Enroll, call heartbeat and policy | Secret returned once; policy defaults disabled | | | | Never record secret |
| P2A-API-02 | Enrolled synthetic device | Replace processes twice; post START/STOP | Removed current row disappears; history remains | | | | No command lines |
| P2A-SHOT-01 | Disposable synthetic PNG/JPEG | Upload and view through Manager route | Private generated files/hashes; unauthenticated read denied | | | | No real screenshots |
| P2A-RET-01 | Disposable expired screenshot | Run `npm run retention:cleanup` | Only confined expired file/metadata removed; audit written | | | | Retention approval required |
| P2A-PRIV-01 | Synthetic dashboard data | Review all labels | Sources are distinct; no employee-activity inference | | | | No productivity score |

## Phase 2B Agent synchronization end-to-end

Use a disposable local enrollment token, synthetic process names/content where possible, and a company-authorized standard Windows test account. Do not record the token or DPAPI-protected file contents as evidence. The current Phase 2A dashboard requires explicit development Manager mode or configured Google authorization; never weaken production Manager access for this test.

1. Start Docker Desktop, then run `docker compose -f docker-compose.dev.yml up -d` and `docker compose -f docker-compose.dev.yml --profile test up -d postgres-test`. Verify both PostgreSQL services are healthy and use separate ports/volumes.
2. In `server`, create the ignored `.env` from `.env.example`, set a disposable enrollment token of at least 32 characters and a private absolute screenshot root, deploy migrations, and start the central server.
3. Set the Agent's `XUGAR_SERVER_SYNC_ENABLED=true`, `XUGAR_SERVER_BASE_URL=http://localhost:3000`, and matching disposable `XUGAR_ENROLLMENT_TOKEN`. Keep the committed screenshot interval at 300 seconds unless a short manual-test override is clearly recorded.
4. Start the visible WPF Agent as a standard non-administrator user. Verify it remains visible and reports synchronization enabled, then enrolled, without displaying either token or device secret.
5. Open the Manager Dashboard in explicitly authorized development mode and verify exactly one device appears with the expected synthetic/test hostname and Agent/OS metadata.
6. Wait at least one heartbeat interval. Verify the WPF last-heartbeat field advances and central `lastHeartbeatAt`/online status updates.
7. Verify current process data arrives and replaces prior state rather than creating a historical row for every sample. Confirm command-line arguments are absent.
8. Open Notepad, wait for a process sample, close Notepad, and wait for another sample.
9. Verify one sampled `START` and `STOP` pair appears centrally, allowing for Windows application/PID behavior. Resending the same queued client event ID must not add a duplicate.
10. Configure/assign a policy that enables monitoring and screenshots during a short approved test window. Display only synthetic, non-sensitive content, then allow one normal-desktop screenshot capture.
11. Verify the screenshot metadata/file appears through the authenticated Manager path, has a server-generated storage key/hash, and is not under `server/public`. Retrying its capture ID must not create another row/file.
12. Stop the central server (or disconnect only the test network) without stopping the Agent.
13. Verify the Agent remains running and visibly reports offline/retrying rather than crashing or hiding.
14. Open/close another safe application and confirm canonical local `telemetry.jsonl`, all three process CSV reports, and eligible local screenshots continue according to privacy policy.
15. Verify the WPF pending queue count/bytes increase appropriately; heartbeat/current state should coalesce instead of accumulating every missed interval.
16. Restart the central server/network and wait through retry backoff.
17. Verify last successful upload advances and the persistent queue drains automatically.
18. Verify no duplicate process events, Agent events, or screenshot captures were created after retry.
19. Close/restart the Agent. Verify the same installation GUID and central device ID are reused and the UI returns to enrolled without issuing a new secret. Do not attempt to decrypt DPAPI content outside the same Windows user.
20. Restart PostgreSQL without deleting volumes. Verify device, event, policy, and screenshot metadata persist.
21. Change the assigned policy version/toggles/window, wait one policy-refresh interval, and verify the WPF policy version/status and effective intervals update.
22. Move outside the approved schedule (or assign a non-current test window). Verify screenshot capture is skipped, a sanitized policy event is recorded, and no unrestricted screenshot begins when the server/policy is unavailable or the cache expires.
23. Lock Windows for at least one eligible short test interval and, separately if approved, show a UAC secure-desktop prompt containing no sensitive content. Verify no lock-screen/UAC screenshot is captured and normal capture resumes later only when policy permits.
24. Stop and Start monitoring from the visible UI. Verify both local monitoring and synchronization pause/resume cleanly and queued data survives the stop/restart.
25. Disable synchronization (`XUGAR_SERVER_SYNC_ENABLED=false`) and restart. Verify the Agent launches with no server, uses the local 300/60 defaults, writes JSONL/CSV/PNG locally, and shows server sync disabled.

Record these additional focused checks:

| Test ID | Setup | Steps | Expected | Actual | PASS/FAIL | Evidence | Notes |
|---|---|---|---|---|---|---|---|
| P2B-DPAPI-01 | Same Windows user | Enroll, restart Agent, inspect only file type/permissions | Credential persists as opaque DPAPI bytes; secret absent from logs/UI/reports | | | | Do not publish bytes |
| P2B-DPAPI-02 | Separate disposable Windows account | Attempt to reuse copied credential | Other user cannot decrypt `CurrentUser` data; Agent degrades safely | | | | No credential workarounds |
| P2B-QUEUE-01 | Server stopped | Restart Agent with pending uploads | Queue survives and item/byte totals remain bounded | | | | No unrelated local deletion |
| P2B-AUTH-01 | Revoke test device | Allow next request | UI shows authentication error; no automatic enrollment storm | | | | Restore deliberately |
| P2B-POLICY-01 | No assigned policy | Start synchronized Agent | Screenshots denied; local process reports continue safely | | | | Disabled default is not permission |
| P2B-POLICY-02 | Overnight timezone window | Test before/after midnight | Window follows configured timezone and end-exclusive semantics | | | | Record UTC/local times |
| P2B-PRIV-01 | Review local and central records | Search for passwords/tokens/headers/command lines | None are present; process presence is not labeled activity time | | | | No productivity inference |
