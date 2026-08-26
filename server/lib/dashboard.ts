import { prisma } from "@/lib/prisma";

export function isDeviceOnline(
  lastHeartbeatAt: Date | null,
  now: Date,
  offlineMinutes: number,
): boolean {
  return Boolean(
    lastHeartbeatAt &&
      lastHeartbeatAt.getTime() >= now.getTime() - offlineMinutes * 60_000,
  );
}

export async function getDashboardOverview(now = new Date()) {
  const offlineMinutes = Number(process.env.XUGAR_DEVICE_OFFLINE_MINUTES ?? "5");
  const cutoff = new Date(now.getTime() - offlineMinutes * 60_000);
  const [totalDevices, onlineDevices, screenshots, processEvents, activTrakAlarms, integration] =
    await prisma.$transaction([
      prisma.device.count({ where: { isRevoked: false } }),
      prisma.device.count({ where: { isRevoked: false, lastHeartbeatAt: { gte: cutoff } } }),
      prisma.screenshot.findMany({
        take: 8,
        orderBy: { capturedAt: "desc" },
        include: { device: { select: { hostname: true } } },
      }),
      prisma.processEvent.findMany({
        take: 10,
        orderBy: { occurredAt: "desc" },
        include: { device: { select: { hostname: true } } },
      }),
      prisma.activTrakAlarmEvent.findMany({ take: 10, orderBy: { occurredAt: "desc" } }),
      prisma.activTrakIntegration.findFirst({ orderBy: { updatedAt: "desc" } }),
    ]);
  return {
    totalDevices,
    onlineDevices,
    offlineDevices: Math.max(0, totalDevices - onlineDevices),
    screenshots,
    processEvents,
    activTrakAlarms,
    integration,
    offlineMinutes,
  };
}

export async function getDeviceList(now = new Date()) {
  const offlineMinutes = Number(process.env.XUGAR_DEVICE_OFFLINE_MINUTES ?? "5");
  const devices = await prisma.device.findMany({
    orderBy: { hostname: "asc" },
    include: {
      screenshots: { take: 1, orderBy: { capturedAt: "desc" }, select: { capturedAt: true } },
      _count: { select: { activTrakAlarmEvents: true } },
    },
  });
  return devices.map((device) => ({
    ...device,
    online: isDeviceOnline(device.lastHeartbeatAt, now, offlineMinutes),
    lastScreenshotAt: device.screenshots[0]?.capturedAt ?? null,
    activTrakMapping: device._count.activTrakAlarmEvents > 0 ? "Matched event" : "Unmapped",
  }));
}
