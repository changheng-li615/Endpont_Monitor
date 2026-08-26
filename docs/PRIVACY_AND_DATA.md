# Privacy and Data — Phase 2B Boundary

## Source-of-truth separation

| Data | Source of truth |
|---|---|
| Device registration, Agent health/version/configuration | Xugar |
| Approved periodic Xugar screenshots | Xugar |
| Complete process presence and START/STOP samples | Xugar |
| Active application/site activity and usage duration | ActivTrak |
| Active/passive status, productivity and workforce analytics | ActivTrak |
| ActivTrak alarms and supported actions | ActivTrak |

Xugar must not derive productivity scores, employee activity time, or hours worked from process presence, foreground samples, heartbeat, or online/offline state.

## Transparency

The MVP is visible. It runs as a normal user application titled **Xugar Endpoint Monitor**, displays its current state and collection intervals, and provides development Stop and Start controls. It has no stealth mode, service installation, autostart persistence, or hidden tray-only behavior.

Use is intended only on company-owned Windows laptops with company authorization, an approved employee policy, and appropriate notice. Technical implementation does not replace legal, HR, security, or management review.

## Data collected

Process snapshots contain:

- UTC capture timestamp;
- device name;
- current Windows domain/user name;
- operating-system and agent version;
- process name and PID;
- executable path when Windows permits access;
- file and product version when accessible;
- working-set bytes when accessible;
- foreground-window status when accessible.

Screenshots contain the pixels visible on each normal interactive monitor at capture time. Screenshot telemetry contains capture timestamp, one-based monitor index, local file path, and pixel dimensions. Operational JSONL records contain monitoring, success/failure, count, skip, and retention-cleanup events.

Daily `process-current.csv`, `process-events.csv`, and `process-summary.csv` files are human-readable derivatives of process telemetry. JSONL remains canonical. Process presence samples are not exact usage duration, a background process does not prove employee activity, and foreground sample counts are only approximate indicators. `Application`, `System`, and `Unknown` categories are reporting labels, not security classifications.

Executable paths and screenshots can contain personal or confidential information. The 24-hour default is a prototype setting, not a substitute for an approved retention policy.

## Data explicitly not collected

The Phase 1 MVP:

- does not keylog;
- does not record clipboard content or history;
- does not record microphone or webcam data;
- does not collect typed passwords, browser credentials, tokens, or other credentials;
- does not collect process command-line arguments;
- does not collect browser history;
- does not attempt secure-desktop or UAC capture or bypass;
- does not terminate, suspend, block, or otherwise control processes;
- does not disable or evade security software.

Publisher/signature information is not implemented in this phase.

## Storage and access

The Agent always stores Phase 1 data locally first. Server synchronization is optional and disabled by default. Its default root is:

```text
%LOCALAPPDATA%\Xugar\EndpointMonitor\Data
```

PNG images are stored separately from JSONL; screenshot pixel data is never logged as text. The application does not add application-level encryption in Phase 1 and relies on Windows account permissions and any organization-managed full-disk protection. Access controls and encryption requirements must be reviewed before wider use.

Retention cleanup defaults to 24 hours, skips filesystem links/reparse points, refuses a whole filesystem root, and tolerates inaccessible files. A file in use or otherwise inaccessible can survive past its cutoff until a later cleanup succeeds. Identity, DPAPI credential, policy cache, and bounded queue live under `sync`; Phase 1 retention excludes that subtree and the queue enforces its own item/byte/age limits.

When explicitly enabled, the Phase 2B Agent enrolls and synchronizes approved heartbeats, current processes, START/STOP events, screenshots, and bounded health events to the Phase 2A server. PostgreSQL stores device metadata, hash-only server credential verifiers, heartbeats, current processes, lifecycle/Agent events, screenshot metadata, policy, normalized ActivTrak placeholders, and Manager audit records. Screenshot images remain in private configured filesystem storage, never PostgreSQL or `public`.

Central policy is privacy-denying for screenshots: missing, malformed, expired, disabled, or out-of-window policy does not authorize a new capture. A still-fresh cached policy can support offline operation within its approved schedule. Local process JSONL/CSV may continue during a temporary policy outage, but it is not queued for central synchronization without valid permission. Heartbeat is operational health and is not employee activity evidence.

Enrollment tokens, device secrets, authorization headers, OAuth secrets, and webhook tokens must not be logged or persisted in plaintext. Screenshot access requires Manager authorization and creates an audit event. Development Manager authentication is not suitable for employee data or public deployment.

## Required review before expansion

Before deployment beyond controlled prototyping, management should explicitly approve:

- the screenshot purpose, interval, and employee notice;
- every process and device field;
- the retention period and deletion expectations;
- who may access local data;
- Windows encryption and endpoint-protection requirements;
- legal, privacy, employment, and works-council obligations where applicable.
- central screenshot/process/heartbeat/diagnostic/audit retention periods;
- Manager roles and Google Workspace OAuth deployment;
- Xugar and ActivTrak schedules, which must not be assumed identical.
