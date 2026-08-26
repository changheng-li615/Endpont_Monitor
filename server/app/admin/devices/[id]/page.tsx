import Link from "next/link";
import Image from "next/image";
import { notFound } from "next/navigation";
import { SourceBadge } from "@/components/source-badge";
import { prisma } from "@/lib/prisma";
import { categorizeProcessPath, type HumanProcessCategory } from "@/lib/process-identity";
import { uuidSchema } from "@/lib/schemas";

type PageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ processes?: string }>;
};

export default async function DevicePage({ params, searchParams }: PageProps) {
  const { id } = await params;
  if (!uuidSchema.safeParse(id).success) notFound();
  const requestedFilter = (await searchParams).processes;
  const filter: HumanProcessCategory | "All" = requestedFilter === "Application" || requestedFilter === "System" ? requestedFilter : "All";
  const device = await prisma.device.findUnique({
    where: { id },
    include: {
      monitoringPolicy: { include: { scheduleWindows: true } },
      screenshots: { take: 12, orderBy: { capturedAt: "desc" } },
      currentProcesses: { orderBy: [{ isForeground: "desc" }, { processName: "asc" }] },
      processEvents: { take: 30, orderBy: { occurredAt: "desc" } },
      activTrakAlarmEvents: { take: 20, orderBy: { occurredAt: "desc" } },
    },
  });
  if (!device) notFound();
  const processes = device.currentProcesses
    .map((process) => ({ ...process, category: categorizeProcessPath(process.executablePath) }))
    .filter((process) => filter === "All" || process.category === filter);
  return (
    <>
      <header className="page-header"><div><div className="eyebrow">DEVICE DETAIL</div><h1>{device.hostname}</h1><p>{device.workEmail ?? device.windowsUser ?? "No user mapped"}</p></div></header>
      <section className="metrics device-facts">
        <article className="metric-card"><span>Agent</span><strong>{device.agentVersion}</strong><small>{device.isRevoked ? "Revoked" : "Enrolled"}</small></article>
        <article className="metric-card"><span>OS</span><strong>{device.osVersion}</strong></article>
        <article className="metric-card"><span>Last seen</span><strong className="small-value">{device.lastSeenAt.toLocaleString()}</strong></article>
        <article className="metric-card"><span>Policy</span><strong className="small-value">{device.monitoringPolicy?.name ?? "No policy — monitoring disabled"}</strong></article>
      </section>
      <section className="panel">
        <div className="panel-title"><h2>Xugar screenshots</h2><SourceBadge source="Xugar" /></div>
        {device.screenshots.length === 0 ? <p className="empty">No screenshots uploaded.</p> : <div className="thumbnail-grid">{device.screenshots.map((shot) => <figure key={shot.id}><Image unoptimized width={320} height={180} src={`/api/admin/screenshots/${shot.id}`} alt={`Monitor ${shot.monitorIndex}`} /><figcaption>Monitor {shot.monitorIndex} · {shot.capturedAt.toLocaleString()}</figcaption></figure>)}</div>}
      </section>
      <section className="panel table-wrap">
        <div className="panel-title"><h2>Current process state</h2><SourceBadge source="Xugar" /></div>
        <div className="filters">{["Application", "System", "All"].map((value) => <Link className={filter === value ? "active" : ""} key={value} href={`/admin/devices/${id}?processes=${value}`}>{value}s</Link>)}</div>
        <table><thead><tr><th>Process</th><th>Category</th><th>PID</th><th>Memory MB</th><th>Foreground</th><th>Path</th><th>Observed</th></tr></thead><tbody>{processes.map((process) => <tr key={process.id}><td>{process.processName}</td><td>{process.category}</td><td>{process.pid}</td><td>{process.workingSetMb?.toFixed(1) ?? "—"}</td><td>{process.isForeground ? "Yes" : "No"}</td><td className="path-cell">{process.executablePath ?? "—"}</td><td>{process.observedAt.toLocaleString()}</td></tr>)}</tbody></table>
        {processes.length === 0 ? <p className="empty">No processes match this view.</p> : null}
        <p className="notice">Process presence is not application usage duration and does not prove employee activity.</p>
      </section>
      <div className="columns">
        <section className="panel"><div className="panel-title"><h2>Process events</h2><SourceBadge source="Xugar" /></div>{device.processEvents.length === 0 ? <p className="empty">No events received.</p> : <ul className="event-list">{device.processEvents.map((event) => <li key={event.id}><b>{event.eventType}</b> {event.processName} (PID {event.pid})<small>{event.occurredAt.toLocaleString()}</small></li>)}</ul>}</section>
        <section className="panel"><div className="panel-title"><h2>ActivTrak</h2><SourceBadge source="ActivTrak" /></div>{device.activTrakAlarmEvents.length === 0 ? <p className="empty">No mapped ActivTrak alarms. Phase 2C will add normalized webhook ingestion.</p> : <ul className="event-list">{device.activTrakAlarmEvents.map((event) => <li key={event.id}><b>{event.alarmName}</b><small>{event.occurredAt.toLocaleString()}</small></li>)}</ul>}</section>
      </div>
    </>
  );
}
