import Link from "next/link";
import { notFound } from "next/navigation";
import { PaginationNav } from "@/components/pagination-nav";
import { ScreenshotGallery } from "@/components/screenshot-gallery";
import { SourceBadge } from "@/components/source-badge";
import { getDeviceDetail } from "@/lib/device-detail";
import {
  buildDeviceDetailHref,
  EVENT_PAGE_SIZES,
  formatAgentVersion,
  formatWindowsVersion,
  PROCESS_PAGE_SIZES,
  queryValue,
  SCREENSHOT_PAGE_SIZES,
  type QueryValues,
} from "@/lib/device-detail-view";
import { uuidSchema } from "@/lib/schemas";

type PageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<QueryValues>;
};

const memoryFormat = new Intl.NumberFormat("en-AU", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

export default async function DevicePage({ params, searchParams }: PageProps) {
  const { id } = await params;
  if (!uuidSchema.safeParse(id).success) notFound();
  const query = await searchParams;
  const detail = await getDeviceDetail(id, query);
  if (!detail) notFound();

  const {
    device,
    processCategory,
    groupedProcesses,
    processPagination,
    eventType,
    eventSearch,
    processEvents,
    eventPagination,
    screenshots,
    screenshotPagination,
    activTrakAlarmEvents,
  } = detail;
  const agentVersion = formatAgentVersion(device.agentVersion);
  const windowsVersion = formatWindowsVersion(device.osVersion);
  const preservedSearchParameters = Object.entries(query)
    .filter(([key]) => !["eventPage", "eventType", "eventSearch"].includes(key))
    .map(([key, value]) => [key, queryValue(value)] as const)
    .filter((entry): entry is readonly [string, string] => Boolean(entry[1]));

  return (
    <>
      <header className="page-header">
        <div>
          <div className="eyebrow">DEVICE DETAIL</div>
          <h1>{device.hostname}</h1>
          <p>{device.workEmail ?? device.windowsUser ?? "No user mapped"}</p>
        </div>
      </header>

      <section className="metrics device-facts" aria-label="Device summary">
        <article className="metric-card summary-card" title={agentVersion.raw ?? undefined}>
          <span>Agent</span>
          <strong className="summary-value">{agentVersion.primary}</strong>
          {agentVersion.secondary ? <small className="build-value">{agentVersion.secondary}</small> : null}
          <small>{device.isRevoked ? "Revoked" : "Enrolled"}</small>
        </article>
        <article className="metric-card summary-card" title={windowsVersion.raw ?? undefined}>
          <span>OS</span>
          <strong className="summary-value">{windowsVersion.primary}</strong>
          {windowsVersion.secondary ? <small className="build-value">{windowsVersion.secondary}</small> : null}
        </article>
        <article className="metric-card summary-card">
          <span>Last seen</span>
          <strong className="small-value">{device.lastSeenAt.toLocaleString()}</strong>
        </article>
        <article className="metric-card summary-card">
          <span>Policy</span>
          <strong className="small-value">{device.monitoringPolicy?.name ?? "No policy - monitoring disabled"}</strong>
        </article>
      </section>

      <section className="panel screenshot-panel">
        <div className="panel-title">
          <div><h2>Xugar screenshots</h2><p>{screenshotPagination.totalItems} captured image{screenshotPagination.totalItems === 1 ? "" : "s"}</p></div>
          <SourceBadge source="Xugar" />
        </div>
        {screenshots.length === 0 ? (
          <p className="empty">No screenshots uploaded.</p>
        ) : (
          <ScreenshotGallery
            deviceName={device.hostname}
            screenshots={screenshots.map((shot) => ({
              ...shot,
              capturedAt: shot.capturedAt.toISOString(),
              capturedLabel: shot.capturedAt.toLocaleString(),
            }))}
          />
        )}
        <PaginationNav
          deviceId={id}
          query={query}
          pagination={screenshotPagination}
          pageParameter="screenshotPage"
          pageSizeParameter="screenshotPageSize"
          pageSizes={SCREENSHOT_PAGE_SIZES}
          label="Screenshot gallery pages"
        />
      </section>

      <section className="panel table-wrap">
        <div className="panel-title">
          <div><h2>Current process state</h2><p>Grouped from the latest raw process instances</p></div>
          <SourceBadge source="Xugar" />
        </div>
        <div className="filters" aria-label="Current process category">
          {([
            ["applications", "Applications"],
            ["systems", "Systems"],
            ["all", "All"],
          ] as const).map(([value, label]) => (
            <Link
              className={processCategory === value ? "active" : ""}
              key={value}
              href={buildDeviceDetailHref(id, query, { processCategory: value, processPage: 1 })}
            >
              {label}
            </Link>
          ))}
        </div>
        <table className="process-table">
          <thead><tr><th>Application / Process</th><th>Instances</th><th>Category</th><th>Total memory</th><th>Foreground</th><th>Path</th><th>Observed</th></tr></thead>
          <tbody>
            {groupedProcesses.map((process) => (
              <tr key={process.identity}>
                <td><strong>{process.displayName} ({process.instanceCount})</strong><small>{process.processName}</small></td>
                <td>{process.instanceCount}</td>
                <td><span className={`process-category category-${process.category.toLocaleLowerCase("en-US")}`}>{process.category}</span></td>
                <td>{process.totalMemoryMb === null ? "-" : `${memoryFormat.format(process.totalMemoryMb)} MiB`}</td>
                <td><span className={process.isForeground ? "foreground yes" : "foreground"}>{process.isForeground ? "Yes" : "No"}</span></td>
                <td className="path-cell" title={process.executablePath ?? undefined}>{process.executablePath ?? "-"}</td>
                <td>{process.observedAt.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {groupedProcesses.length === 0 ? <p className="empty">No processes match this view.</p> : null}
        <PaginationNav
          deviceId={id}
          query={query}
          pagination={processPagination}
          pageParameter="processPage"
          pageSizeParameter="processPageSize"
          pageSizes={PROCESS_PAGE_SIZES}
          label="Current process pages"
        />
        <p className="notice">Process presence is not application usage duration and does not prove employee activity.</p>
      </section>

      <section className="panel table-wrap">
        <div className="panel-title">
          <div><h2>Process events</h2><p>Newest events first</p></div>
          <SourceBadge source="Xugar" />
        </div>
        <div className="event-toolbar">
          <div className="filters" aria-label="Process event type">
            {(["all", "start", "stop"] as const).map((value) => (
              <Link
                className={eventType === value ? "active" : ""}
                key={value}
                href={buildDeviceDetailHref(id, query, { eventType: value, eventPage: 1 })}
              >
                {value === "all" ? "All" : value.toUpperCase()}
              </Link>
            ))}
          </div>
          <form className="event-search" method="get">
            {preservedSearchParameters.map(([key, value]) => <input key={key} type="hidden" name={key} value={value} />)}
            <input type="hidden" name="eventPage" value="1" />
            <input type="hidden" name="eventType" value={eventType} />
            <label htmlFor="event-search">Process name</label>
            <input id="event-search" name="eventSearch" defaultValue={eventSearch} maxLength={100} placeholder="Search processes" />
            <button type="submit">Search</button>
            {eventSearch ? <Link href={buildDeviceDetailHref(id, query, { eventSearch: null, eventPage: 1 })}>Clear</Link> : null}
          </form>
        </div>
        <table className="event-table">
          <thead><tr><th>Event</th><th>Process</th><th>PID</th><th>Time</th></tr></thead>
          <tbody>
            {processEvents.map((event) => (
              <tr key={event.id}>
                <td><span className={`event-type event-${event.eventType.toLocaleLowerCase("en-US")}`}>{event.eventType}</span></td>
                <td>{event.processName}</td>
                <td>{event.pid}</td>
                <td>{event.occurredAt.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {processEvents.length === 0 ? <p className="empty">No events match this view.</p> : null}
        <PaginationNav
          deviceId={id}
          query={query}
          pagination={eventPagination}
          pageParameter="eventPage"
          pageSizeParameter="eventPageSize"
          pageSizes={EVENT_PAGE_SIZES}
          label="Process event pages"
        />
      </section>

      <section className="panel">
        <div className="panel-title"><h2>ActivTrak</h2><SourceBadge source="ActivTrak" /></div>
        {activTrakAlarmEvents.length === 0 ? (
          <p className="empty">Phase 2C is not configured. No mapped ActivTrak alarms.</p>
        ) : (
          <ul className="event-list">
            {activTrakAlarmEvents.map((event) => <li key={event.id}><b>{event.alarmName}</b><small>{event.occurredAt.toLocaleString()}</small></li>)}
          </ul>
        )}
      </section>
    </>
  );
}
