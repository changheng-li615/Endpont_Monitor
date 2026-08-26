import { authenticateDevice } from "@/lib/device-auth";
import { HttpError, readBoundedJson, routeError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { createProcessKey } from "@/lib/process-identity";
import { currentProcessesSchema } from "@/lib/schemas";

type RouteContext = { params: Promise<{ id: string }> };

export async function PUT(request: Request, context: RouteContext): Promise<Response> {
  try {
    const { id } = await context.params;
    const device = await authenticateDevice(request, id);
    const input = await readBoundedJson(request, currentProcessesSchema, 1024 * 1024);
    const observedAt = new Date(input.observedAt);
    const rows = input.processes.map((process) => ({
      ...process,
      executablePath: process.executablePath ?? null,
      productVersion: process.productVersion ?? null,
      workingSetMb: process.workingSetMb ?? null,
      deviceId: device.id,
      observedAt,
      processKey: createProcessKey(process.pid, process.processName, process.executablePath ?? null),
    }));
    if (new Set(rows.map((row) => row.processKey)).size !== rows.length) {
      throw new HttpError(400, "Current process batch contains duplicate identities.");
    }

    await prisma.$transaction(async (transaction) => {
      await transaction.deviceCurrentProcess.deleteMany({ where: { deviceId: device.id } });
      if (rows.length > 0) {
        await transaction.deviceCurrentProcess.createMany({ data: rows });
      }
      await transaction.device.update({
        where: { id: device.id },
        data: { lastSeenAt: new Date() },
      });
    });
    return Response.json({ accepted: rows.length });
  } catch (error) {
    return routeError(error);
  }
}
