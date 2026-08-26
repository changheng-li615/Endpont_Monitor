import Link from "next/link";
import { getDeviceList } from "@/lib/dashboard";

export default async function DevicesPage() {
  const devices = await getDeviceList();
  return (
    <>
      <header className="page-header"><div><div className="eyebrow">XUGAR DEVICES</div><h1>Managed endpoints</h1></div></header>
      <section className="panel table-wrap">
        <table>
          <thead><tr><th>Hostname</th><th>Work user</th><th>Status</th><th>Agent</th><th>OS</th><th>Last seen</th><th>Last screenshot</th><th>ActivTrak mapping</th></tr></thead>
          <tbody>{devices.map((device) => (
            <tr key={device.id}>
              <td><Link href={`/admin/devices/${device.id}`}>{device.hostname}</Link></td>
              <td>{device.workEmail ?? device.windowsUser ?? "—"}</td>
              <td><span className={`status ${device.online ? "online" : "offline"}`}>{device.isRevoked ? "Revoked" : device.online ? "Online" : "Offline"}</span></td>
              <td>{device.agentVersion}</td><td>{device.osVersion}</td>
              <td>{device.lastSeenAt.toLocaleString()}</td>
              <td>{device.lastScreenshotAt?.toLocaleString() ?? "—"}</td>
              <td>{device.activTrakMapping}</td>
            </tr>
          ))}</tbody>
        </table>
        {devices.length === 0 ? <p className="empty">No devices enrolled.</p> : null}
      </section>
      <p className="notice">Online means only that the Xugar Agent sent a recent heartbeat. It does not indicate employee presence or work activity.</p>
    </>
  );
}
