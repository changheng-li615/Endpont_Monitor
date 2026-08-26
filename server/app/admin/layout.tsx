import Link from "next/link";
import { requireManager } from "@/lib/manager-auth";

export const dynamic = "force-dynamic";

export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  const identity = await requireManager();
  return (
    <div className="admin-shell">
      <aside className="sidebar">
        <div className="brand">XUGAR</div>
        <p>Endpoint Management</p>
        <nav>
          <Link href="/admin">Overview</Link>
          <Link href="/admin/devices">Devices</Link>
        </nav>
        <small>Signed in: {identity}</small>
      </aside>
      <main className="content">
        {identity === "development-manager@localhost" ? (
          <div className="development-banner">DEVELOPMENT AUTH ONLY — DO NOT EXPOSE PUBLICLY</div>
        ) : null}
        {children}
      </main>
    </div>
  );
}
