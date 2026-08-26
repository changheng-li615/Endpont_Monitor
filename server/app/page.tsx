import Link from "next/link";

export default function HomePage() {
  return (
    <main className="landing">
      <div className="eyebrow">XUGAR INTERNAL PLATFORM</div>
      <h1>Endpoint Management &amp; Monitoring</h1>
      <p>
        Xugar device health, approved periodic screenshots, and process presence are kept distinct
        from ActivTrak activity analytics.
      </p>
      <Link className="button" href="/admin">Manager dashboard</Link>
      <p className="notice">Manager authentication is required. Development access is explicitly local-only.</p>
    </main>
  );
}
