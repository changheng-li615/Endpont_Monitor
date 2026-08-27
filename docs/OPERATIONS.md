# Operations — Phase 2B.1

## Database lifecycle

```powershell
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml --profile test up -d postgres-test
docker compose -f docker-compose.dev.yml ps
Set-Location server
npm run prisma:migrate:deploy
```

Use tracked migrations only. Do not use `prisma db push`, reset a database, delete a volume, or edit an applied migration. Back up non-disposable databases before later schema changes.

The normal development database binds to loopback port 55432. The disposable integration-test database has its own volume and binds to 55433. `npm test` explicitly loads `.env.test`, safety-checks the host/database name, deploys migrations, and then runs Vitest; do not redirect it to a development, staging, or production database.

## Agent synchronization

Set `XUGAR_SERVER_SYNC_ENABLED=true`, `XUGAR_SERVER_BASE_URL`, and a bootstrap-only `XUGAR_ENROLLMENT_TOKEN` for first enrollment. Local development may use `http://localhost:3000`; all non-loopback deployments require HTTPS. After enrollment, the token can be removed because the DPAPI-protected device credential persists under the configured Agent data root.

The WPF status area reports enrollment, connection, last heartbeat/upload/policy refresh, queue size, and policy state. A `401`/`403` is an authentication degradation: inspect revocation/server identity before performing a deliberate re-enrollment. Do not delete the credential or repeatedly supply the enrollment token as an automatic recovery mechanism.

Offline operation keeps local JSONL/CSV working. Queue entries retry with capped exponential backoff and jitter. Heartbeat/current state coalesce; historical events/screenshots persist within the configured 1,000-item, 100 MiB, 168-hour defaults. Queue-limit and screenshot-drop events appear in local canonical telemetry. Never manually copy queue envelopes between installations.

## Screenshot retention

After setting an approved absolute storage root and retention period, run `npm run retention:cleanup`. The command considers only database-selected expired keys, tolerates missing files, keeps metadata when deletion fails, records an audit summary, and returns failure when any deletion fails. Seven days is development-only.

## Device operations

- Revoked devices receive generic `401` responses and cannot re-enroll.
- Authorized re-enrollment rotates the device secret. Never put it in tickets, logs, CSV, JSONL, or screenshots.
- Current process uploads replace state; historical investigation uses `ProcessEvent`.
- Online/offline means recent/missing heartbeat only.

## Incident handling

If a credential may have leaked, revoke or rotate it before collecting logs. Preserve only bounded audit records and approved evidence. If screenshot confinement or Manager authorization fails, stop the server and block staging/public deployment.

Phase 2B makes no ActivTrak calls. Missing ActivTrak credentials do not affect Xugar server or Agent synchronization; fixture/webhook/live integration begins in Phase 2C.

## Persistent Agent configuration

Use the installed/published executable for one-time configuration. This writes `%LOCALAPPDATA%\Xugar\EndpointMonitor\config.json` and, when requested, the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\XugarEndpointMonitor` value:

```powershell
& 'C:\Program Files\Xugar\EndpointMonitor\Xugar.Endpoint.Agent.exe' `
  --configure `
  --enable-sync `
  --server-url 'https://monitor.example.com' `
  --enable-startup
```

The Run value is a quoted absolute executable path followed by `--startup`. `--configure --disable-startup` removes it idempotently. Normal standard-user operation and HKCU changes require no elevation. Do not point pilot startup at `bin\Debug`, a temporary directory, `dotnet run`, or `dotnet.exe`.

For first enrollment only, set `XUGAR_ENROLLMENT_TOKEN` in the process that starts the Agent. Remove it immediately afterward. Once `Data\sync\device-credential.bin` exists, restart uses the same DPAPI credential and installation ID without the token. Never add the bootstrap token to `config.json`, the Run command, a shortcut, or deployment script.

Effective precedence is command-line configuration, environment variables/aliases, persistent JSON, committed defaults. A malformed/unknown-member persistent file is quarantined as `config.corrupt.*.json` and safe disabled settings are used.

## Tray and shutdown operations

- Normal launch: tray plus status window.
- `--startup`: tray plus background runtime, no initial window.
- Window X: hide only; monitoring and synchronization continue.
- Tray Open or a second normal launch: restore the primary window.
- Tray Exit: cancel monitoring/networking, preserve the bounded disk queue, dispose the tray, and exit.
- Stop/Start buttons: stop/start the coordinator while the transparent Agent process/tray remains. They do not bypass central policy.

There is no watchdog or automatic respawn after explicit Exit. The next Windows sign-in starts the Agent only if HKCU startup remains enabled.

## Pilot publish

From the repository root:

```powershell
.\scripts\publish-agent.ps1
```

The default self-contained `win-x64` output is `artifacts\Xugar.Endpoint.Agent`. Use `-FrameworkDependent` only when the .NET 10 Windows Desktop Runtime is an approved prerequisite. The script does not install files or request elevation. Copy the complete published directory to a stable pilot location such as `C:\Program Files\Xugar\EndpointMonitor` during an authorized deployment step, then run the configuration command from that stable executable. MSI packaging, fleet deployment tooling, and code signing remain later deployment work.
