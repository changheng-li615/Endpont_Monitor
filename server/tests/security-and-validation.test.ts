import { describe, expect, it } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import { SourceBadge } from "@/components/source-badge";
import { isDeviceOnline } from "@/lib/dashboard";
import { getRuntimeEnvironment } from "@/lib/environment";
import { isAllowedGoogleManager, isDevelopmentManagerEnabled } from "@/lib/manager-auth";
import { categorizeProcessPath, createProcessKey } from "@/lib/process-identity";
import {
  currentProcessesSchema,
  enrollmentSchema,
  monitoringPolicyResponseSchema,
  processEventsSchema,
} from "@/lib/schemas";
import {
  createDeviceSecret,
  hashDeviceSecret,
  readBearerToken,
  secretsEqual,
  verifyDeviceSecret,
} from "@/lib/security";

describe("device secrets", () => {
  it("generates random bearer-safe values and stores only a salted hash", async () => {
    const first = createDeviceSecret();
    const second = createDeviceSecret();
    expect(first).not.toBe(second);
    expect(first).toMatch(/^[A-Za-z0-9_-]+$/);
    const hash = await hashDeviceSecret(first);
    expect(hash).not.toContain(first);
    expect(await verifyDeviceSecret(first, hash)).toBe(true);
    expect(await verifyDeviceSecret(second, hash)).toBe(false);
  });

  it("compares enrollment tokens and parses a strict bearer header", () => {
    expect(secretsEqual("same", "same")).toBe(true);
    expect(secretsEqual("same", "different")).toBe(false);
    expect(readBearerToken(new Request("http://localhost", { headers: { authorization: "Bearer secret" } }))).toBe("secret");
    expect(readBearerToken(new Request("http://localhost", { headers: { authorization: "Basic secret" } }))).toBeNull();
  });
});

describe("bounded API schemas", () => {
  const process = { processName: "notepad", pid: 42, executablePath: null, productVersion: null, workingSetMb: null, isForeground: false };

  it("accepts bounded enrollment and rejects extra privacy-expanding fields", () => {
    const valid = { installationId: crypto.randomUUID(), hostname: "XUGAR-LT-02", windowsUser: null, workEmail: "tester@example.invalid", osVersion: "Windows 11", agentVersion: "0.2.0" };
    expect(enrollmentSchema.safeParse(valid).success).toBe(true);
    expect(enrollmentSchema.safeParse({ ...valid, commandLine: "secret" }).success).toBe(false);
  });

  it("accepts nullable process metadata and rejects command lines", () => {
    expect(currentProcessesSchema.safeParse({ observedAt: new Date().toISOString(), processes: [process] }).success).toBe(true);
    expect(currentProcessesSchema.safeParse({ observedAt: new Date().toISOString(), processes: [{ ...process, commandLine: "--token secret" }] }).success).toBe(false);
  });

  it("rejects excessive current process and event batches", () => {
    expect(currentProcessesSchema.safeParse({ observedAt: new Date().toISOString(), processes: Array.from({ length: 513 }, () => process) }).success).toBe(false);
    const event = { occurredAt: new Date().toISOString(), eventType: "START", processName: "notepad", pid: 42 };
    expect(processEventsSchema.safeParse({ events: Array.from({ length: 513 }, () => event) }).success).toBe(false);
  });

  it("allows only START and STOP events", () => {
    const base = { occurredAt: new Date().toISOString(), processName: "notepad", pid: 42 };
    expect(processEventsSchema.safeParse({ events: [{ ...base, eventType: "START" }, { ...base, eventType: "STOP" }] }).success).toBe(true);
    expect(processEventsSchema.safeParse({ events: [{ ...base, eventType: "KILL" }] }).success).toBe(false);
  });

  it("enforces monitoring interval and schedule bounds", () => {
    const policy = { version: 1, monitoringEnabled: true, screenshotEnabled: true, screenshotIntervalSeconds: 300, processEnabled: true, processIntervalSeconds: 60, timezone: "Australia/Sydney", scheduleWindows: [{ dayOfWeek: 1, startLocalTime: "09:00", endLocalTime: "17:00" }] };
    expect(monitoringPolicyResponseSchema.safeParse(policy).success).toBe(true);
    expect(monitoringPolicyResponseSchema.safeParse({ ...policy, screenshotIntervalSeconds: 15 }).success).toBe(false);
    expect(monitoringPolicyResponseSchema.safeParse({ ...policy, scheduleWindows: [{ dayOfWeek: 7, startLocalTime: "09:00", endLocalTime: "17:00" }] }).success).toBe(false);
  });
});

describe("configuration and reporting boundaries", () => {
  it("rejects a volume root for screenshot storage", () => {
    expect(() => getRuntimeEnvironment({ DATABASE_URL: "postgresql://localhost/x", XUGAR_ENROLLMENT_TOKEN: "x".repeat(32), XUGAR_SCREENSHOT_STORAGE_ROOT: "C:\\" })).toThrow(/filesystem root/);
  });

  it("requires explicit development manager mode and never permits it in production", () => {
    expect(isDevelopmentManagerEnabled({ NODE_ENV: "development", XUGAR_MANAGER_AUTH_MODE: "development", XUGAR_DEVELOPMENT_MANAGER: "true" })).toBe(true);
    expect(isDevelopmentManagerEnabled({ NODE_ENV: "production", XUGAR_MANAGER_AUTH_MODE: "development", XUGAR_DEVELOPMENT_MANAGER: "true" })).toBe(false);
  });

  it("requires both Workspace domain and explicit manager allow-list membership", () => {
    const environment = { XUGAR_MANAGER_ALLOWED_DOMAIN: "example.invalid", XUGAR_MANAGER_ALLOWED_EMAILS: "manager@example.invalid" };
    expect(isAllowedGoogleManager("MANAGER@example.invalid", environment)).toBe(true);
    expect(isAllowedGoogleManager("employee@example.invalid", environment)).toBe(false);
    expect(isAllowedGoogleManager("manager@outside.invalid", environment)).toBe(false);
  });

  it("derives stable process keys and conservative human categories", () => {
    expect(createProcessKey(42, "NOTEPAD", "C:/Apps/notepad.exe")).toBe(createProcessKey(42, "notepad", "c:\\apps\\notepad.exe"));
    expect(categorizeProcessPath("C:\\Windows\\System32\\svchost.exe")).toBe("System");
    expect(categorizeProcessPath("C:\\Program Files\\App\\app.exe")).toBe("Application");
    expect(categorizeProcessPath(null)).toBe("Unknown");
  });

  it("defines online status only by the configured heartbeat cutoff", () => {
    const current = new Date("2026-08-25T01:10:00Z");
    expect(isDeviceOnline(new Date("2026-08-25T01:06:00Z"), current, 5)).toBe(true);
    expect(isDeviceOnline(new Date("2026-08-25T01:04:59Z"), current, 5)).toBe(false);
    expect(isDeviceOnline(null, current, 5)).toBe(false);
  });

  it("visually attributes Xugar and ActivTrak data as different sources", () => {
    expect(renderToStaticMarkup(SourceBadge({ source: "Xugar" }))).toContain("Source: Xugar");
    expect(renderToStaticMarkup(SourceBadge({ source: "ActivTrak" }))).toContain("Source: ActivTrak");
  });
});
