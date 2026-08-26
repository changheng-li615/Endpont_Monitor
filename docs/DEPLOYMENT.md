# Deployment — Phase 2A

Phase 2A is a development/staging foundation, not a production deployment.

## Local development

1. Install .NET 10, Node.js 24, and Docker Desktop.
2. Copy `server/.env.example` to ignored `server/.env` and replace placeholders.
3. Run `docker compose -f docker-compose.dev.yml up -d`.
4. In `server`, run `npm ci` and `npm run prisma:migrate:deploy`.
5. For local dashboard work only, explicitly enable development Manager mode.
6. Run `npm run dev` using synthetic data/screenshots only.
7. Stop with `docker compose -f docker-compose.dev.yml down`.

The database binds to loopback port 55432 and uses a named PostgreSQL 18 volume. Never add `--volumes` to routine shutdown.

## Deployment blockers

- Real Google Workspace OAuth and explicit Manager authorization.
- HTTPS, trusted proxy configuration, secret management, rate limiting, CSRF/security headers, and production health/observability.
- Approved screenshot storage permissions and retention.
- Database backup and migration rollback procedures.
- Employee monitoring notice/policy and legal/management approval.
- Supported resolution of the Prisma CLI advisory in `SECURITY.md`.
- Phase 2B Agent networking and Phase 2C ActivTrak ingestion.

Do not expose Phase 2A publicly or claim production readiness.
