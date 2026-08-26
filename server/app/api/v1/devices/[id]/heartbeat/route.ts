import { authenticateDevice } from "@/lib/device-auth";
import { readBoundedJson, routeError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { heartbeatSchema } from "@/lib/schemas";

type RouteContext = { params: Promise<{ id: string }> };

export async function POST(request: Request, context: RouteContext): Promise<Response> {
  try {
    const { id } = await context.params;
    const device = await authenticateDevice(request, id);
    const input = await readBoundedJson(request, heartbeatSchema, 16 * 1024);
    const occurredAt = new Date(input.occurredAt);
    const receivedAt = new Date();
    await prisma.$transaction([
      prisma.deviceHeartbeat.create({
        data: {
          deviceId: device.id,
          occurredAt,
          agentVersion: input.agentVersion,
          osVersion: input.osVersion,
          uptimeSeconds: input.uptimeSeconds == null ? null : BigInt(input.uptimeSeconds),
        },
      }),
      prisma.device.update({
        where: { id: device.id },
        data: {
          lastSeenAt: receivedAt,
          lastHeartbeatAt: receivedAt,
          agentVersion: input.agentVersion,
          osVersion: input.osVersion,
        },
      }),
    ]);
    return Response.json({ accepted: true });
  } catch (error) {
    return routeError(error);
  }
}
