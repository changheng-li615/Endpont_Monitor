# Security — Phase 2A

## Secrets and authentication

- Enrollment, OAuth, database, future webhook, and device secrets are environment-only; `.env` is ignored.
- Device secrets are random 256-bit values returned once and stored as salted scrypt hashes.
- Enrollment comparison uses SHA-256 digests and timing-safe comparison.
- Authorization errors are generic. Request bodies, bearer values, and secret-bearing URLs are not logged.
- Duplicate enrollment rotates the secret; revoked devices cannot authenticate or re-enroll.

## Input and storage controls

- Strict Zod schemas bound strings, arrays, numbers, and enums.
- Current processes are transactionally replaced and capped at 512. No command-line field exists.
- Screenshots require authenticated devices, bounded size, PNG/JPEG MIME/signature agreement, and generated UUID filenames.
- Storage keys are database-owned and confined to an absolute non-root private directory. Filesystem links are rejected before reads/deletes.
- SHA-256 is computed server-side; pixels are never in PostgreSQL or `public`.
- Retention removes metadata only after deletion or confirmation that the file is missing.

## Manager boundary

Manager access defaults to disabled. Development mode requires two settings and refuses production. Google mode requires OAuth credentials, Auth.js secret, Workspace domain, and explicit Manager email allow-list. Screenshot responses are private/no-store and audited.

Production deployment remains blocked until real Google OAuth is manually verified behind HTTPS.

## Known dependency advisory

Prisma 7.9.1 currently pulls `deepmerge-ts` 7.1.5, reported as `GHSA-ggr8-5vv4-36mx` for recursive-object stack exhaustion. Prisma configuration is a trusted local migration/build input, not a server request path. Do not use `npm audit fix --force` or an unsupported override; adopt a supported patched Prisma release before production hardening.

## Explicit exclusions

There is no remote shell, arbitrary command execution, process termination/suspension, stealth persistence, keylogging, clipboard/content/credential capture, microphone/webcam access, security bypass, or productivity scoring.
