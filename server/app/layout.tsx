import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Xugar Endpoint Management",
  description: "Authorized Xugar endpoint health and monitoring management platform.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
