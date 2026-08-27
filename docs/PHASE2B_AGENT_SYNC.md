# Phase 2B Agent Synchronization

## Boundary and data flow

Phase 2B connects only the visible user-session Xugar Agent to the Xugar central platform. Phase 2B.1 adds a visible tray/background runtime and current-user sign-in launch around that same synchronization layer. ActivTrak, remote control, enforcement, automatic process actions, hidden monitoring, and Windows Service screenshot capture are outside this phase.

```text
capture/sample
  -> canonical local JSONL / derived CSV / local PNG
  -> policy eligibility check
  -> durable bounded queue
  -> authenticated Xugar API
```

Network or server failure cannot turn a successful local write into a failed monitoring iteration. With synchronization disabled, the Agent retains its Phase 1 behavior, 300-second screenshot default, 60-second process default, and no server dependency.

## Enrollment and credential lifecycle

`FileInstallationIdentityStore` generates one random GUID and atomically persists it under `Data\sync\installation-id`. It is not derived from a username, email address, IP/MAC address, serial number, or hardware fingerprint. An invalid identity file is preserved with a `corrupt` suffix before recovery.

When no credential exists, `DeviceEnrollmentService` sends installation/device metadata to `POST /api/v1/devices/enroll` with the bootstrap enrollment token. A valid existing credential prevents enrollment on every startup. Successful response data is validated and persisted before use.

`FileDeviceCredentialStore` serializes only device ID/secret in memory, passes it through `WindowsDpapiDeviceCredentialProtector`, clears temporary plaintext/protected buffers where practical, and atomically writes opaque bytes to `Data\sync\device-credential.bin`. DPAPI uses `DataProtectionScope.CurrentUser`, matching the interactive Agent's ownership. The bootstrap token, device secret, and authorization header are never written to JSONL, CSV, UI, or logs. A corrupt credential is quarantined and can be recovered only through authorized enrollment.

Phase 2B.1 startup first reads this protected credential. If it is valid, `DeviceEnrollmentService` returns it before inspecting `ServerSync:EnrollmentToken`, so restart does not require the bootstrap token and cannot create another enrollment record. If both credential and token are absent, the Agent remains visibly not enrolled/configuration-degraded and uses bounded retry timing rather than an enrollment storm.

## Configuration

Committed synchronization defaults are safe and disabled:

| Environment alias | Configuration key | Default |
|---|---|---:|
| `XUGAR_SERVER_SYNC_ENABLED` | `ServerSync:Enabled` | `false` |
| `XUGAR_SERVER_BASE_URL` | `ServerSync:BaseUrl` | `http://localhost:3000` |
| `XUGAR_ALLOW_INSECURE_LOCALHOST` | `ServerSync:AllowInsecureLocalhost` | `true` |
| `XUGAR_ENROLLMENT_TOKEN` | `ServerSync:EnrollmentToken` | empty |
| `XUGAR_HEARTBEAT_INTERVAL_SECONDS` | `ServerSync:HeartbeatIntervalSeconds` | 60 |
| `XUGAR_POLICY_REFRESH_SECONDS` | `ServerSync:PolicyRefreshIntervalSeconds` | 300 |
| `XUGAR_POLICY_MAX_AGE_SECONDS` | `ServerSync:PolicyMaxAgeSeconds` | 900 |
| `XUGAR_UPLOAD_BATCH_SIZE` | `ServerSync:UploadBatchSize` | 100 |
| `XUGAR_QUEUE_MAX_ITEMS` | `ServerSync:QueueMaxItems` | 1,000 |
| `XUGAR_QUEUE_MAX_BYTES` | `ServerSync:QueueMaxBytes` | 104,857,600 |
| `XUGAR_QUEUE_MAX_AGE_HOURS` | `ServerSync:QueueMaxAgeHours` | 168 |

.NET double-underscore environment keys remain supported. Non-loopback HTTP is rejected even when the development flag is true. Production configuration must use HTTPS. Certificate validation is never disabled.

Phase 2B.1 persists the non-secret counterparts of these settings in `%LOCALAPPDATA%\Xugar\EndpointMonitor\config.json`. Precedence is command line, environment/aliases, persistent JSON, committed defaults. `EnrollmentToken` is deliberately absent from the persistent schema. Use `Xugar.Endpoint.Agent.exe --configure --enable-sync --server-url <url> --enable-startup` for one-time pilot setup, then supply `XUGAR_ENROLLMENT_TOKEN` only to the first enrollment launch.

## Policy and schedule

Policy GET is retried but never queued. A valid response is atomically cached with the local UTC retrieval time. The cache is usable until `PolicyMaxAgeSeconds`; the current API has no server-supplied expiry.

- Standalone mode: local Phase 1 settings permit local collection; nothing is synchronized.
- Valid enabled policy, enabled activity, inside schedule: local collection and synchronization are allowed at the policy interval.
- Valid disabled/out-of-window policy: the affected collection is paused.
- Missing, invalid, expired, or unavailable policy: screenshots are denied; local process JSONL/CSV continues at the local interval but is not synchronized.
- Heartbeat remains operational health and is not gated as employee activity.

Schedule evaluation converts the UTC clock through the policy's explicit timezone, uses Sunday=0 through Saturday=6 as specified by the API, treats the end minute as exclusive, and supports windows spanning midnight. Empty windows do not authorize capture. The current server policy has separate screenshot/process toggles and intervals but one common schedule-window set; independent per-telemetry schedule sets remain a known future contract limitation.

The normal input-desktop/device-unlocked checks remain inside `WindowsScreenshotCapture`, so central permission cannot bypass lock-screen or UAC/secure-desktop denial.

## Queue and retries

`FileUploadQueue` stores JSON envelopes and separate payloads under `Data\sync\queue`. Writes use same-directory temporary files and atomic moves. Screenshot bytes are copied into the queue after their local PNG is safely written. Payload paths are generated and confined beneath the configured data root.

Data-type behavior:

- Heartbeat: keep only the newest pending item.
- Current processes: keep only the newest pending state, capped to the API's 512 records; no command lines.
- Process events: preserve sampled START/STOP history in batches and retain stable client event UUIDs.
- Screenshots: preserve private PNG/JPEG copies with stable capture UUIDs while within queue limits.
- Agent events: preserve bounded sanitized operational events with stable client event UUIDs.
- Policy GET: retry retrieval without queueing.

The queue expires old entries, handles corrupt envelopes by bounded quarantine, deletes orphan payloads, and enforces item/byte limits. Eviction priority is oldest heartbeat/current state, Agent events, screenshots, then process events. Screenshot eviction records a local sanitized queue-limit event; it never deletes the original Phase 1 report/image as part of queue cleanup.

Retryable connection/timeout/408/429/5xx failures use exponential backoff with ±20% jitter and a configured cap. Authentication failures retain the item, show a degraded state, and do not automatically clear credentials or spam re-enrollment. Invalid/non-retryable payloads are discarded with a local health event so they cannot poison the queue indefinitely. Cancellation cleanly stops loops.

## Idempotency

Process and Agent events carry `clientEventId`; screenshots carry `captureId`. Phase 2B adds nullable server columns and per-device unique indexes through the tracked `20260826030000_phase2b_idempotency` migration. Existing Phase 2A clients remain compatible because IDs are optional. New Agent retries reuse the ID stored in the queue payload/envelope, preventing duplicate history.

## Visible status and background lifecycle

The tray icon remains visible while the Agent process runs. A normal launch shows the WPF status window; `--startup` begins the same coordinator without showing it. Window Close hides the window while heartbeat, policy refresh, capture, and queue loops continue. Tray Open restores the existing window and tray Exit performs graceful cancellation. The window reports whether synchronization is enabled, enrollment state, server connection/authentication state, last heartbeat/upload/policy refresh, queue item/byte totals, and current policy status. It never renders the enrollment token, device secret, authorization header, database credentials, or screenshot content.

## Test database bootstrap

Start the isolated database before server tests:

```powershell
docker compose -f docker-compose.dev.yml --profile test up -d postgres-test
Set-Location server
npm test
```

Vitest and the Prisma pretest command explicitly load `server/.env.test`. The loader refuses a non-loopback host or database name without `_test`. `npm test` deploys tracked migrations automatically, then integration tests may safely clear only this disposable database. No manual PowerShell `DATABASE_URL` assignment is needed.

## Known limitations

- DPAPI behavior and the complete WPF/server/offline workflow require manual testing under the intended standard Windows user.
- A different Windows account cannot decrypt a `CurrentUser` credential by design.
- Central policy assignment/editing still uses Phase 2A database/admin operations; Phase 2B does not add a policy editor.
- Queue delivery is sequential and intentionally simple for the current bounded endpoint volume.
- START/STOP events remain sampling-based; processes entirely between snapshots may be missed.
- No Phase 2C ActivTrak ingestion or correlation is present.
