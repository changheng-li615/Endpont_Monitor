# Security — Phase 2B

## Secrets and authentication

- Enrollment, OAuth, database, future webhook, and device secrets are environment-only; `.env` is ignored.
- Device secrets are random 256-bit values returned once and stored as salted scrypt hashes.
- Enrollment comparison uses SHA-256 digests and timing-safe comparison.
- Authorization errors are generic. Request bodies, bearer values, and secret-bearing URLs are not logged.
- Duplicate enrollment rotates the secret; revoked devices cannot authenticate or re-enroll.
- The Agent stores only the server-issued per-device credential, protected with Windows DPAPI `DataProtectionScope.CurrentUser`. The bootstrap enrollment token remains configuration-only and is not the long-term credential.
- DPAPI `CurrentUser` is appropriate because networking currently belongs to the visible interactive Agent. A future Service-owned credential requires a reviewed migration; Phase 2B does not weaken scope or copy plaintext secrets.
- Production Agent endpoints require HTTPS. Plain HTTP is accepted only for explicitly enabled loopback development, and TLS certificate validation is never bypassed.
- Typed client errors contain status/category only and never include bearer tokens, authorization headers, submitted payloads, or response bodies.

## Input and storage controls

- Strict Zod schemas bound strings, arrays, numbers, and enums.
- Current processes are transactionally replaced and capped at 512. No command-line field exists.
- Screenshots require authenticated devices, bounded size, PNG/JPEG MIME/signature agreement, and generated UUID filenames.
- Storage keys are database-owned and confined to an absolute non-root private directory. Filesystem links are rejected before reads/deletes.
- SHA-256 is computed server-side; pixels are never in PostgreSQL or `public`.
- Retention removes metadata only after deletion or confirmation that the file is missing.
- Durable queue paths are generated beneath the configured Agent data root. Atomic envelope/payload writes, item/byte/age bounds, deterministic eviction, corrupt-envelope quarantine, and orphan-payload cleanup prevent unbounded or path-escaping storage.
- Process and Agent events use client UUIDs with per-device unique constraints; screenshots use per-device capture UUIDs. Retried historical uploads therefore do not duplicate server rows/files.

## Manager boundary

Manager access defaults to disabled. Development mode requires two settings and refuses production. Google mode requires OAuth credentials, Auth.js secret, Workspace domain, and explicit Manager email allow-list. Screenshot responses are private/no-store and audited.

Production deployment remains blocked until real Google OAuth is manually verified behind HTTPS.

## Test database boundary

Vitest loads `server/.env.test` explicitly. The loader refuses non-loopback PostgreSQL hosts and database names that do not end in `_test`; `npm test` deploys migrations to this isolated database before executing destructive integration-test cleanup. The committed token/database values are local test-only placeholders, not deployable credentials.

## Explicit exclusions

There is no remote shell, arbitrary command execution, process termination/suspension, stealth persistence, keylogging, clipboard/content/credential capture, microphone/webcam access, security bypass, or productivity scoring.
