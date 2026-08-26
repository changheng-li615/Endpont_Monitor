import { createHash, randomUUID } from "node:crypto";
import { rm } from "node:fs/promises";
import path from "node:path";
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { POST as enrollRoute } from "@/app/api/v1/devices/enroll/route";
import { POST as heartbeatRoute } from "@/app/api/v1/devices/[id]/heartbeat/route";
import { PUT as currentProcessesRoute } from "@/app/api/v1/devices/[id]/processes/current/route";
import { POST as processEventsRoute } from "@/app/api/v1/devices/[id]/process-events/route";
import { GET as policyRoute } from "@/app/api/v1/devices/[id]/policy/route";
import { POST as screenshotsRoute } from "@/app/api/v1/devices/[id]/screenshots/route";
import { prisma } from "@/lib/prisma";
import { verifyDeviceSecret } from "@/lib/security";
import { getDashboardOverview, getDeviceList } from "@/lib/dashboard";

const enrollmentToken = "phase2a-test-enrollment-token-not-production";
const screenshotRoot = path.resolve("runtime", "integration-screenshots");
const now = "2026-08-25T01:02:03.000Z";

type Enrollment = { deviceId: string; deviceSecret: string };

function jsonRequest(url: string, method: string, body: unknown, token?: string, deviceId?: string): Request {
  const headers = new Headers({ "content-type": "application/json" });
  if (token) headers.set("authorization", `Bearer ${token}`);
  if (deviceId) headers.set("x-xugar-device-id", deviceId);
  return new Request(url, { method, headers, body: JSON.stringify(body) });
}

async function enroll(installationId = randomUUID()): Promise<Enrollment> {
  const response = await enrollRoute(jsonRequest("http://localhost/api/v1/devices/enroll", "POST", {
    installationId, hostname: "XUGAR-TEST", windowsUser: "tester", workEmail: "tester@example.invalid", osVersion: "Windows 11", agentVersion: "0.2.0",
  }, enrollmentToken));
  expect([200, 201]).toContain(response.status);
  return response.json() as Promise<Enrollment>;
}

function context(deviceId: string) {
  return { params: Promise.resolve({ id: deviceId }) };
}

beforeAll(() => {
  process.env.XUGAR_ENROLLMENT_TOKEN = enrollmentToken;
  process.env.XUGAR_SCREENSHOT_STORAGE_ROOT = screenshotRoot;
  process.env.XUGAR_SCREENSHOT_RETENTION_DAYS = "7";
  process.env.XUGAR_SCREENSHOT_MAX_BYTES = "10485760";
  process.env.XUGAR_DEVICE_OFFLINE_MINUTES = "5";
});

beforeEach(async () => {
  await prisma.$transaction([
    prisma.auditEvent.deleteMany(), prisma.activTrakAlarmEvent.deleteMany(), prisma.activTrakIntegration.deleteMany(),
    prisma.agentEvent.deleteMany(), prisma.screenshot.deleteMany(), prisma.processEvent.deleteMany(),
    prisma.deviceCurrentProcess.deleteMany(), prisma.deviceHeartbeat.deleteMany(), prisma.monitoringScheduleWindow.deleteMany(),
    prisma.device.deleteMany(), prisma.monitoringPolicy.deleteMany(),
  ]);
  await rm(screenshotRoot, { recursive: true, force: true });
});

afterAll(async () => {
  await rm(screenshotRoot, { recursive: true, force: true });
  await prisma.$disconnect();
});

describe("enrollment", () => {
  it("enrolls with a one-time plaintext secret while storing only its hash", async () => {
    const result = await enroll();
    const stored = await prisma.device.findUniqueOrThrow({ where: { id: result.deviceId } });
    expect(stored.deviceSecretHash).not.toContain(result.deviceSecret);
    expect(await verifyDeviceSecret(result.deviceSecret, stored.deviceSecretHash)).toBe(true);
  });

  it("rejects an invalid enrollment token without logging it", async () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const response = await enrollRoute(jsonRequest("http://localhost/api/v1/devices/enroll", "POST", { installationId: randomUUID() }, "wrong-secret"));
    expect(response.status).toBe(401);
    expect(await prisma.device.count()).toBe(0);
    expect(log).not.toHaveBeenCalled();
    log.mockRestore();
  });

  it("reuses a duplicate installation identity and rotates its secret", async () => {
    const installationId = randomUUID();
    const first = await enroll(installationId);
    const second = await enroll(installationId);
    expect(second.deviceId).toBe(first.deviceId);
    expect(second.deviceSecret).not.toBe(first.deviceSecret);
    expect(await prisma.device.count()).toBe(1);
    const stored = await prisma.device.findUniqueOrThrow({ where: { id: first.deviceId } });
    expect(await verifyDeviceSecret(first.deviceSecret, stored.deviceSecretHash)).toBe(false);
    expect(await verifyDeviceSecret(second.deviceSecret, stored.deviceSecretHash)).toBe(true);
  });

  it("does not re-enroll a revoked installation", async () => {
    const installationId = randomUUID();
    const first = await enroll(installationId);
    await prisma.device.update({ where: { id: first.deviceId }, data: { isRevoked: true } });
    const response = await enrollRoute(jsonRequest("http://localhost/api/v1/devices/enroll", "POST", { installationId, hostname: "X", windowsUser: null, workEmail: null, osVersion: "Windows 11", agentVersion: "0.2.0" }, enrollmentToken));
    expect(response.status).toBe(403);
  });
});

describe("device authentication and heartbeat", () => {
  it("accepts a valid heartbeat and updates last-seen fields", async () => {
    const device = await enroll();
    const beforeRequest = Date.now();
    const response = await heartbeatRoute(jsonRequest("http://localhost/heartbeat", "POST", { occurredAt: now, agentVersion: "0.2.1", osVersion: "Windows 11", uptimeSeconds: 123 }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(200);
    const stored = await prisma.device.findUniqueOrThrow({ where: { id: device.deviceId } });
    expect(stored.lastHeartbeatAt?.getTime()).toBeGreaterThanOrEqual(beforeRequest);
    expect(stored.lastHeartbeatAt?.getTime()).toBeLessThanOrEqual(Date.now());
    expect(stored.lastSeenAt.getTime()).toBe(stored.lastHeartbeatAt?.getTime());
    expect(await prisma.deviceHeartbeat.count()).toBe(1);
    expect((await prisma.deviceHeartbeat.findFirstOrThrow()).occurredAt.toISOString()).toBe(now);
  });

  it.each([
    ["wrong secret", "wrong", null],
    ["mismatched device ID", null, randomUUID()],
  ])("rejects %s", async (_name, tokenOverride, headerOverride) => {
    const device = await enroll();
    const response = await heartbeatRoute(jsonRequest("http://localhost/heartbeat", "POST", { occurredAt: now, agentVersion: "0.2.0", osVersion: "Windows 11" }, tokenOverride ?? device.deviceSecret, headerOverride ?? device.deviceId), context(device.deviceId));
    expect(response.status).toBe(401);
  });

  it("rejects a revoked device", async () => {
    const device = await enroll();
    await prisma.device.update({ where: { id: device.deviceId }, data: { isRevoked: true } });
    const response = await heartbeatRoute(jsonRequest("http://localhost/heartbeat", "POST", { occurredAt: now, agentVersion: "0.2.0", osVersion: "Windows 11" }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(401);
  });
});

describe("current processes and lifecycle events", () => {
  const notepad = { processName: "notepad", pid: 100, executablePath: null, productVersion: null, workingSetMb: null, isForeground: true };
  const calc = { processName: "calc", pid: 101, executablePath: "C:\\Apps\\calc.exe", productVersion: "1.0", workingSetMb: 25.5, isForeground: false };

  it("transactionally replaces current state and removes absent processes", async () => {
    const device = await enroll();
    let response = await currentProcessesRoute(jsonRequest("http://localhost/processes", "PUT", { observedAt: now, processes: [notepad, calc] }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(200);
    response = await currentProcessesRoute(jsonRequest("http://localhost/processes", "PUT", { observedAt: now, processes: [notepad] }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(200);
    const rows = await prisma.deviceCurrentProcess.findMany();
    expect(rows.map((row) => row.processName)).toEqual(["notepad"]);
    expect(rows[0]?.executablePath).toBeNull();
  });

  it("rejects an excessive process batch", async () => {
    const device = await enroll();
    const response = await currentProcessesRoute(jsonRequest("http://localhost/processes", "PUT", { observedAt: now, processes: Array.from({ length: 513 }, (_, pid) => ({ ...notepad, pid })) }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(400);
  });

  it("stores only START and STOP event batches", async () => {
    const device = await enroll();
    const response = await processEventsRoute(jsonRequest("http://localhost/events", "POST", { events: [{ occurredAt: now, eventType: "START", ...notepad }, { occurredAt: now, eventType: "STOP", ...notepad }] }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(202);
    expect((await prisma.processEvent.findMany({ orderBy: { createdAt: "asc" } })).map((event) => event.eventType)).toEqual(["START", "STOP"]);
  });

  it("rejects invalid lifecycle events", async () => {
    const device = await enroll();
    const response = await processEventsRoute(jsonRequest("http://localhost/events", "POST", { events: [{ occurredAt: now, eventType: "KILL", ...notepad }] }, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(400);
  });
});

describe("monitoring policy", () => {
  it("returns privacy-safe disabled defaults when no valid policy exists", async () => {
    const device = await enroll();
    const response = await policyRoute(jsonRequest("http://localhost/policy", "GET", undefined, device.deviceSecret, device.deviceId), context(device.deviceId));
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ version: 0, monitoringEnabled: false, screenshotEnabled: false, screenshotIntervalSeconds: 300, processEnabled: false, processIntervalSeconds: 60, timezone: "UTC", scheduleWindows: [] });
  });

  it("serializes assigned schedule windows and preserves the 300-second screenshot default", async () => {
    const device = await enroll();
    const policy = await prisma.monitoringPolicy.create({ data: { name: "Business hours", monitoringEnabled: true, screenshotEnabled: true, processEnabled: true, timezone: "Australia/Sydney", scheduleWindows: { create: [{ dayOfWeek: 1, startLocalTime: "09:00", endLocalTime: "17:00" }] } } });
    await prisma.device.update({ where: { id: device.deviceId }, data: { monitoringPolicyId: policy.id } });
    const response = await policyRoute(jsonRequest("http://localhost/policy", "GET", undefined, device.deviceSecret, device.deviceId), context(device.deviceId));
    const body = await response.json();
    expect(body.screenshotIntervalSeconds).toBe(300);
    expect(body.scheduleWindows).toEqual([{ dayOfWeek: 1, startLocalTime: "09:00", endLocalTime: "17:00" }]);
  });
});

describe("dashboard queries", () => {
  it("returns enrolled devices and keeps Xugar and ActivTrak query results separate", async () => {
    const device = await enroll();
    await prisma.processEvent.create({ data: { deviceId: device.deviceId, occurredAt: new Date(now), eventType: "START", processName: "notepad", pid: 10 } });
    await prisma.activTrakAlarmEvent.create({ data: { alarmName: "Synthetic alarm", occurredAt: new Date(now), mappingStatus: "MATCHED", mappedDeviceId: device.deviceId } });
    const devices = await getDeviceList(new Date("2026-08-25T01:10:00Z"));
    const overview = await getDashboardOverview(new Date("2026-08-25T01:10:00Z"));
    expect(devices).toHaveLength(1);
    expect(devices[0]?.activTrakMapping).toBe("Matched event");
    expect(overview.processEvents.map((event) => event.processName)).toEqual(["notepad"]);
    expect(overview.activTrakAlarms.map((event) => event.alarmName)).toEqual(["Synthetic alarm"]);
  });
});

describe("screenshot upload", () => {
  function screenshotRequest(device: Enrollment, bytes: Buffer, mimeType: string, fileName = "untrusted-name.png") {
    const form = new FormData();
    form.set("capturedAt", now); form.set("monitorIndex", "1"); form.set("width", "100"); form.set("height", "50");
    form.set("file", new File([Uint8Array.from(bytes)], fileName, { type: mimeType }));
    return new Request("http://localhost/screenshots", { method: "POST", headers: { authorization: `Bearer ${device.deviceSecret}`, "x-xugar-device-id": device.deviceId }, body: form });
  }

  it.each([
    ["PNG", Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 0]), "image/png"],
    ["JPEG", Buffer.from([0xff, 0xd8, 0, 0, 0xff, 0xd9]), "image/jpeg"],
  ])("stores a validated %s with generated path and server hash", async (_label, bytes, mimeType) => {
    const device = await enroll();
    const response = await screenshotsRoute(screenshotRequest(device, bytes, mimeType, "../../employee-file-name.png"), context(device.deviceId));
    expect(response.status).toBe(201);
    const row = await prisma.screenshot.findFirstOrThrow();
    expect(row.storageKey).not.toContain("employee-file-name");
    expect(row.sha256).toBe(createHash("sha256").update(bytes).digest("hex"));
  });

  it("rejects an invalid MIME type and content", async () => {
    const device = await enroll();
    const response = await screenshotsRoute(screenshotRequest(device, Buffer.from("not an image"), "image/gif"), context(device.deviceId));
    expect(response.status).toBe(400);
    expect(await prisma.screenshot.count()).toBe(0);
  });

  it("rejects oversized and unauthorized uploads", async () => {
    const device = await enroll();
    process.env.XUGAR_SCREENSHOT_MAX_BYTES = "8";
    let response = await screenshotsRoute(screenshotRequest(device, Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 0]), "image/png"), context(device.deviceId));
    expect(response.status).toBe(400);
    process.env.XUGAR_SCREENSHOT_MAX_BYTES = "10485760";
    response = await screenshotsRoute(screenshotRequest({ ...device, deviceSecret: "wrong" }, Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 0]), "image/png"), context(device.deviceId));
    expect(response.status).toBe(401);
  });
});
