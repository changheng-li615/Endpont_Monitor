import { prisma } from "@/lib/prisma";
import {
  createPagination,
  EVENT_PAGE_SIZES,
  groupCurrentProcesses,
  paginate,
  parseEventType,
  parseProcessCategory,
  PROCESS_PAGE_SIZES,
  processMatchesCategory,
  queryValue,
  SCREENSHOT_PAGE_SIZES,
  type QueryValues,
} from "@/lib/device-detail-view";

export async function getDeviceDetail(deviceId: string, query: QueryValues) {
  const processCategory = parseProcessCategory(queryValue(query.processCategory));
  const eventType = parseEventType(queryValue(query.eventType));
  const eventSearch = (queryValue(query.eventSearch)?.trim() ?? "").slice(0, 100);
  const eventWhere = {
    deviceId,
    ...(eventType === "all" ? {} : { eventType: eventType.toUpperCase() as "START" | "STOP" }),
    ...(eventSearch ? { processName: { contains: eventSearch, mode: "insensitive" as const } } : {}),
  };

  const [device, currentProcessRows, eventTotal, screenshotTotal] = await prisma.$transaction([
    prisma.device.findUnique({
      where: { id: deviceId },
      select: {
        id: true,
        hostname: true,
        windowsUser: true,
        workEmail: true,
        osVersion: true,
        agentVersion: true,
        lastSeenAt: true,
        isRevoked: true,
        monitoringPolicy: {
          include: { scheduleWindows: true },
        },
      },
    }),
    prisma.deviceCurrentProcess.findMany({
      where: { deviceId },
      select: {
        id: true,
        processName: true,
        pid: true,
        executablePath: true,
        productVersion: true,
        workingSetMb: true,
        isForeground: true,
        observedAt: true,
      },
    }),
    prisma.processEvent.count({ where: eventWhere }),
    prisma.screenshot.count({ where: { deviceId } }),
  ]);

  if (!device) return null;

  const groupedProcesses = groupCurrentProcesses(currentProcessRows)
    .filter((process) => processMatchesCategory(process, processCategory));
  const processPagination = createPagination(
    queryValue(query.processPage),
    queryValue(query.processPageSize),
    groupedProcesses.length,
    PROCESS_PAGE_SIZES,
    25,
  );
  const eventPagination = createPagination(
    queryValue(query.eventPage),
    queryValue(query.eventPageSize),
    eventTotal,
    EVENT_PAGE_SIZES,
    25,
  );
  const screenshotPagination = createPagination(
    queryValue(query.screenshotPage),
    queryValue(query.screenshotPageSize),
    screenshotTotal,
    SCREENSHOT_PAGE_SIZES,
    12,
  );

  const [processEvents, screenshots, activTrakAlarmEvents] = await prisma.$transaction([
    prisma.processEvent.findMany({
      where: eventWhere,
      select: {
        id: true,
        eventType: true,
        processName: true,
        pid: true,
        occurredAt: true,
      },
      orderBy: [{ occurredAt: "desc" }, { id: "desc" }],
      skip: eventPagination.skip,
      take: eventPagination.pageSize,
    }),
    prisma.screenshot.findMany({
      where: { deviceId },
      select: {
        id: true,
        capturedAt: true,
        monitorIndex: true,
        width: true,
        height: true,
      },
      orderBy: [{ capturedAt: "desc" }, { monitorIndex: "asc" }, { id: "desc" }],
      skip: screenshotPagination.skip,
      take: screenshotPagination.pageSize,
    }),
    prisma.activTrakAlarmEvent.findMany({
      where: { mappedDeviceId: deviceId },
      select: { id: true, alarmName: true, occurredAt: true },
      orderBy: [{ occurredAt: "desc" }, { id: "desc" }],
      take: 20,
    }),
  ]);

  return {
    device,
    processCategory,
    groupedProcesses: paginate(groupedProcesses, processPagination),
    processPagination,
    eventType,
    eventSearch,
    processEvents,
    eventPagination,
    screenshots,
    screenshotPagination,
    activTrakAlarmEvents,
  };
}
