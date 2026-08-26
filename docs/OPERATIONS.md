# Operations — Phase 2B

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
