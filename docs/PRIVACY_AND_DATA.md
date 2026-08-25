# Privacy and Data — Phase 1

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

Phase 1 stores data locally only. There is no backend, cloud upload, remote management, API, database, or network telemetry path. The default root is:

```text
%LOCALAPPDATA%\Xugar\EndpointMonitor\Data
```

PNG images are stored separately from JSONL; screenshot pixel data is never logged as text. The application does not add application-level encryption in Phase 1 and relies on Windows account permissions and any organization-managed full-disk protection. Access controls and encryption requirements must be reviewed before wider use.

Retention cleanup defaults to 24 hours, skips filesystem links/reparse points, refuses a whole filesystem root, and tolerates inaccessible files. A file in use or otherwise inaccessible can survive past its cutoff until a later cleanup succeeds.

## Required review before expansion

Before Phase 2 or deployment beyond controlled prototyping, management should explicitly approve:

- the screenshot purpose, interval, and employee notice;
- every process and device field;
- the retention period and deletion expectations;
- who may access local data;
- Windows encryption and endpoint-protection requirements;
- legal, privacy, employment, and works-council obligations where applicable.

