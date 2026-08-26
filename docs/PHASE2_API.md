# Phase 2 API — Phase 2B-compatible contract

All endpoints use JSON unless stated otherwise. Request objects are strict and bounded; unknown fields such as command-line arguments are rejected. Errors do not echo secrets or submitted telemetry.

## Authentication

Enrollment requires `Authorization: Bearer <XUGAR_ENROLLMENT_TOKEN>`. The enrollment token cannot authenticate telemetry APIs.

Every later device endpoint requires:

```text
Authorization: Bearer <deviceSecret>
X-Xugar-Device-Id: <deviceId>
```

The path ID, header ID, hash-verified secret, and non-revoked record must match. Authentication failures return generic `401` responses.

## Endpoints

### `POST /api/v1/devices/enroll`

Accepts a stable installation UUID plus bounded hostname, nullable Windows user/work email, OS version, and Agent version. It creates or safely reuses the installation identity, generates a random secret, stores only its salted scrypt hash, and returns plaintext once. Duplicate authorized enrollment rotates the secret. Revoked installations return `403`.

### `POST /api/v1/devices/{id}/heartbeat`

Accepts occurrence time, Agent/OS versions, and nullable nonnegative uptime. It adds heartbeat history and updates health fields from server receipt time so endpoint clock skew cannot distort online status. Online means only a recent heartbeat.

### `PUT /api/v1/devices/{id}/processes/current`

Accepts at most 512 processes with name, PID, nullable path/version/memory, and foreground flag. Command lines are forbidden. A bounded identity key is derived and current rows are transactionally replaced.

### `POST /api/v1/devices/{id}/process-events`

Accepts 1–512 `START`/`STOP` records. Other lifecycle types are rejected. Phase 2B supplies optional `clientEventId` UUIDs. `(deviceId, clientEventId)` is unique and duplicate retries are ignored. Historical events are separate from current state.

### `POST /api/v1/devices/{id}/screenshots`

Accepts multipart `file`, `capturedAt`, `monitorIndex`, optional dimensions, and optional `captureId`. Only bounded PNG/JPEG content with matching signature is accepted. The filename is ignored; the server creates a confined key and SHA-256. `(deviceId, captureId)` is unique: an identical retry returns the existing result, while different content with the same ID returns `409`. Binary data is outside PostgreSQL.

### `POST /api/v1/devices/{id}/events`

Accepts up to 100 bounded `INFO`, `WARNING`, or `ERROR` operational events. Optional `clientEventId` UUIDs are deduplicated per device. It is not an unrestricted log endpoint.

### `GET /api/v1/devices/{id}/policy`

Returns a versioned policy and local-time schedule windows. Without a valid assigned policy, monitoring, screenshots, and processes are disabled. The 300-second interval remains present but is not permission to capture.

## Manager screenshot route

`GET /api/admin/screenshots/{id}` requires an authorized Manager, resolves only the database-owned key beneath the configured root, streams private/no-store content, and audits the view. Browser-provided filesystem paths are never accepted.

## Agent behavior

The Phase 2B client uses this contract without inventing another authentication mechanism. It coalesces heartbeat/current state, gives durable history stable UUIDs, and does not queue policy GET requests. HTTP is loopback-development-only; production uses HTTPS.

## Deferred

ActivTrak webhooks, fixture ingestion, ActivConnect, and live calls remain Phase 2C.
