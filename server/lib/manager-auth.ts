import { redirect } from "next/navigation";
import type { EnvironmentSource } from "@/lib/environment";

export type ManagerAuthMode = "disabled" | "development" | "google";

export function getManagerAuthMode(source: EnvironmentSource = process.env): ManagerAuthMode {
  const value = source.XUGAR_MANAGER_AUTH_MODE?.toLowerCase() ?? "disabled";
  return value === "development" || value === "google" ? value : "disabled";
}

export function isAllowedGoogleManager(
  email: string | null | undefined,
  source: EnvironmentSource = process.env,
): boolean {
  if (!email) {
    return false;
  }
  const normalized = email.trim().toLowerCase();
  const domain = source.XUGAR_MANAGER_ALLOWED_DOMAIN?.trim().toLowerCase();
  const allowedEmails = new Set(
    (source.XUGAR_MANAGER_ALLOWED_EMAILS ?? "")
      .split(",")
      .map((value) => value.trim().toLowerCase())
      .filter(Boolean),
  );
  return Boolean(domain) && normalized.endsWith(`@${domain}`) && allowedEmails.has(normalized);
}

export function isDevelopmentManagerEnabled(
  source: EnvironmentSource = process.env,
): boolean {
  return (
    source.NODE_ENV !== "production" &&
    getManagerAuthMode(source) === "development" &&
    source.XUGAR_DEVELOPMENT_MANAGER === "true"
  );
}

export async function getManagerIdentity(): Promise<string | null> {
  if (isDevelopmentManagerEnabled()) {
    return "development-manager@localhost";
  }
  if (getManagerAuthMode() !== "google") {
    return null;
  }
  const { auth } = await import("@/auth");
  const session = await auth();
  return isAllowedGoogleManager(session?.user?.email) ? session?.user?.email ?? null : null;
}

export async function requireManager(): Promise<string> {
  const identity = await getManagerIdentity();
  if (!identity) {
    redirect("/api/auth/signin?callbackUrl=/admin");
  }
  return identity;
}
