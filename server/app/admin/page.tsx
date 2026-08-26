import Link from "next/link";
import Image from "next/image";
import { MetricCard } from "@/components/metric-card";
import { SourceBadge } from "@/components/source-badge";
import { getDashboardOverview } from "@/lib/dashboard";

export default async function AdminOverviewPage() {
  const overview = await getDashboardOverview();
  return (
    <>
      <header className="page-header">
        <div><div className="eyebrow">MANAGER DASHBOARD</div><h1>Fleet overview</h1></div>
        <Link className="button secondary" href="/admin/devices">View devices</Link>
      </header>
      <section className="metrics">
        <MetricCard label="Devices" value={overview.totalDevices} />
        <MetricCard label="Online agents" value={overview.onlineDevices} note={`Heartbeat within ${overview.offlineMinutes} min`} />
        <MetricCard label="Offline agents" value={overview.offlineDevices} note="This is not an employee activity status" />
        <MetricCard label="ActivTrak" value={overview.integration?.enabled ? overview.integration.mode : "Disabled"} />
      </section>
      <section className="panel">
        <div className="panel-title"><h2>Recent Xugar screenshots</h2><SourceBadge source="Xugar" /></div>
        {overview.screenshots.length === 0 ? <p className="empty">No screenshots have been uploaded.</p> : (
          <div className="thumbnail-grid">{overview.screenshots.map((item) => (
            <figure key={item.id}>
              <Image unoptimized width={320} height={180} src={`/api/admin/screenshots/${item.id}`} alt={`Screenshot from ${item.device.hostname}`} />
              <figcaption>{item.device.hostname} · {item.capturedAt.toLocaleString()}</figcaption>
            </figure>
          ))}</div>
        )}
      </section>
      <div className="columns">
        <section className="panel">
          <div className="panel-title"><h2>Process events</h2><SourceBadge source="Xugar" /></div>
          {overview.processEvents.length === 0 ? <p className="empty">No process events received.</p> : (
            <ul className="event-list">{overview.processEvents.map((event) => (
              <li key={event.id}><b>{event.eventType}</b> {event.processName}<small>{event.device.hostname} · {event.occurredAt.toLocaleString()}</small></li>
            ))}</ul>
          )}
        </section>
        <section className="panel">
          <div className="panel-title"><h2>ActivTrak alarms</h2><SourceBadge source="ActivTrak" /></div>
          {overview.activTrakAlarms.length === 0 ? <p className="empty">No normalized ActivTrak alarms. Ingestion begins in Phase 2C.</p> : (
            <ul className="event-list">{overview.activTrakAlarms.map((event) => (
              <li key={event.id}><b>{event.alarmName}</b><small>{event.mappingStatus} · {event.occurredAt.toLocaleString()}</small></li>
            ))}</ul>
          )}
        </section>
      </div>
    </>
  );
}
