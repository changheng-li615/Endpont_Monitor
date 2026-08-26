import { authenticateDevice } from "@/lib/device-auth";
import { readBoundedJson, routeError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { agentEventsSchema } from "@/lib/schemas";

type RouteContext = { params: Promise<{ id: string }> };

export async function POST(request: Request, context: RouteContext): Promise<Response> {
  try {
    const { id } = await context.params;
    const device = await authenticateDevice(request, id);
    const input = await readBoundedJson(request, agentEventsSchema, 256 * 1024);
    const rows = input.events.map((event) => ({
      ...event,
      clientEventId: event.clientEventId ?? null,
      occurredAt: new Date(event.occurredAt),
      deviceId: device.id,
    }));
    await prisma.$transaction([
      prisma.agentEvent.createMany({ data: rows, skipDuplicates: true }),
      prisma.device.update({ where: { id: device.id }, data: { lastSeenAt: new Date() } }),
    ]);
    return Response.json({ accepted: rows.length }, { status: 202 });
  } catch (error) {
    return routeError(error);
  }
}
