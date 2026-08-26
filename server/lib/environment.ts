import path from "node:path";
import { z } from "zod";

const positiveInteger = (fallback: number) =>
  z.coerce.number().int().positive().default(fallback);

const runtimeSchema = z.object({
  DATABASE_URL: z.string().min(1),
  XUGAR_ENROLLMENT_TOKEN: z.string().min(32).max(512),
  XUGAR_SCREENSHOT_STORAGE_ROOT: z.string().min(1),
  XUGAR_SCREENSHOT_RETENTION_DAYS: positiveInteger(7).pipe(z.number().max(3650)),
  XUGAR_SCREENSHOT_MAX_BYTES: positiveInteger(10 * 1024 * 1024).pipe(
    z.number().max(50 * 1024 * 1024),
  ),
  XUGAR_DEVICE_OFFLINE_MINUTES: positiveInteger(5).pipe(z.number().max(1440)),
});

export type RuntimeEnvironment = z.infer<typeof runtimeSchema>;
export type EnvironmentSource = Record<string, string | undefined>;

export function getRuntimeEnvironment(
  source: EnvironmentSource = process.env,
): RuntimeEnvironment {
  const parsed = runtimeSchema.safeParse(source);
  if (!parsed.success) {
    const names = parsed.error.issues.map((issue) => issue.path.join(".")).join(", ");
    throw new Error(`Invalid or missing server configuration: ${names}`);
  }

  const databaseUrl = new URL(parsed.data.DATABASE_URL);
  if (!new Set(["postgres:", "postgresql:"]).has(databaseUrl.protocol)) {
    throw new Error("DATABASE_URL must use PostgreSQL.");
  }

  const storageRoot = path.resolve(parsed.data.XUGAR_SCREENSHOT_STORAGE_ROOT);
  if (!path.isAbsolute(parsed.data.XUGAR_SCREENSHOT_STORAGE_ROOT)) {
    throw new Error("XUGAR_SCREENSHOT_STORAGE_ROOT must be absolute.");
  }
  if (path.parse(storageRoot).root === storageRoot) {
    throw new Error("XUGAR_SCREENSHOT_STORAGE_ROOT cannot be a filesystem root.");
  }

  return {
    ...parsed.data,
    XUGAR_SCREENSHOT_STORAGE_ROOT: storageRoot,
  };
}

export function requireDatabaseUrl(source: EnvironmentSource = process.env): string {
  const value = source.DATABASE_URL;
  if (!value) {
    throw new Error("DATABASE_URL is required.");
  }
  const parsed = new URL(value);
  if (!new Set(["postgres:", "postgresql:"]).has(parsed.protocol)) {
    throw new Error("DATABASE_URL must use PostgreSQL.");
  }
  return value;
}
