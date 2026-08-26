# Operations — Phase 2A

## Database lifecycle

```powershell
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps
Set-Location server
npm run prisma:migrate:deploy
```

Use tracked migrations only. Do not use `prisma db push`, reset a database, delete a volume, or edit an applied migration. Back up non-disposable databases before later schema changes.

## Screenshot retention

After setting an approved absolute storage root and retention period, run `npm run retention:cleanup`. The command considers only database-selected expired keys, tolerates missing files, keeps metadata when deletion fails, records an audit summary, and returns failure when any deletion fails. Seven days is development-only.

## Device operations

- Revoked devices receive generic `401` responses and cannot re-enroll.
- Authorized re-enrollment rotates the device secret. Never put it in tickets, logs, CSV, JSONL, or screenshots.
- Current process uploads replace state; historical investigation uses `ProcessEvent`.
- Online/offline means recent/missing heartbeat only.

## Incident handling

If a credential may have leaked, revoke or rotate it before collecting logs. Preserve only bounded audit records and approved evidence. If screenshot confinement or Manager authorization fails, stop the server and block staging/public deployment.

Phase 2A makes no ActivTrak calls. Missing ActivTrak credentials do not affect Xugar server operation; fixture mode begins in Phase 2C.
