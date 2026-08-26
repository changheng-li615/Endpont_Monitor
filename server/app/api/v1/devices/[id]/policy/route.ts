import { authenticateDevice } from "@/lib/device-auth";
import { routeError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { monitoringPolicyResponseSchema } from "@/lib/schemas";

type RouteContext = { params: Promise<{ id: string }> };

const safeMissingPolicy = {
  version: 0,
  monitoringEnabled: false,
  screenshotEnabled: false,
  screenshotIntervalSeconds: 300,
  processEnabled: false,
  processIntervalSeconds: 60,
  timezone: "UTC",
  scheduleWindows: [],
};

export async function GET(request: Request, context: RouteContext): Promise<Response> {
  try {
    const { id } = await context.params;
    const device = await authenticateDevice(request, id);
    const policy = device.monitoringPolicyId
      ? await prisma.monitoringPolicy.findUnique({
          where: { id: device.monitoringPolicyId },
          include: { scheduleWindows: { orderBy: [{ dayOfWeek: "asc" }, { startLocalTime: "asc" }] } },
        })
      : null;
    if (!policy) {
      return Response.json(safeMissingPolicy);
    }
    const response = monitoringPolicyResponseSchema.parse({
      version: policy.version,
      monitoringEnabled: policy.monitoringEnabled,
      screenshotEnabled: policy.screenshotEnabled,
      screenshotIntervalSeconds: policy.screenshotIntervalSeconds,
      processEnabled: policy.processEnabled,
      processIntervalSeconds: policy.processIntervalSeconds,
      timezone: policy.timezone,
      scheduleWindows: policy.scheduleWindows.map((window) => ({
        dayOfWeek: window.dayOfWeek,
        startLocalTime: window.startLocalTime,
        endLocalTime: window.endLocalTime,
      })),
    });
    return Response.json(response);
  } catch (error) {
    return routeError(error);
  }
}
