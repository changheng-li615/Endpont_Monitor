# ActivTrak Integration Boundary

## Source of truth

| Capability | Xugar | ActivTrak |
|---|---:|---:|
| Device registration, Agent health/version/policy | Source | No |
| Approved fixed periodic screenshots | Source | Alarm screenshots stay in ActivTrak |
| Complete/background process presence and START/STOP | Source | No duplicate analytics |
| Active application/site usage and duration | No competing analytics | Source |
| Active/passive and productivity/category analytics | No scoring | Source |
| ActivTrak alarms/actions | Normalize/reference only | Source |

## Modes

- `disabled`: no integration; Phase 2A default.
- `fixture`: synthetic `@example.invalid` data for Phase 2C development/tests.
- `live`: only after entitlement, official contract, and real secret configuration are verified.

Phase 2A stores non-secret configuration and a bounded normalized alarm model only. It does not expose a webhook, contact ActivTrak, scrape its UI, automate browser sessions, or import ActivTrak screenshots.

## Planned webhook and mapping

Phase 2C will prioritize Alarm External Notifications to a tokenized route. The opaque token must be random, revocable, rotatable, redacted from logs, and hash-only where practical. Only approved normalized fields may persist; unrestricted raw payloads must not.

Mapping will prefer explicit computer identifiers and work emails. Results are `MATCHED`, `UNMATCHED`, or `AMBIGUOUS`; ambiguous records are never silently mapped.

## Optional ActivConnect

ActivConnect is optional and read-only. It may be added only after entitlement and the current official API contract are confirmed. No endpoints are invented. ActivTrak screenshots remain in ActivTrak; only a trusted deep link may be shown later.

External requirements include the actual ActivTrak package, approved sample payload, webhook secret configuration, optional ActivConnect subscription, and deliberate schedule alignment. Xugar never broadens tracking because ActivTrak schedule data is unavailable.
