import type { Device } from "@/generated/prisma/client";
import { HttpError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { readBearerToken, verifyDeviceSecret } from "@/lib/security";
import { uuidSchema } from "@/lib/schemas";

export async function authenticateDevice(
  request: Request,
  routeDeviceId: string,
): Promise<Device> {
  const headerDeviceId = request.headers.get("x-xugar-device-id");
  const secret = readBearerToken(request);
  const parsedRouteId = uuidSchema.safeParse(routeDeviceId);
  const parsedHeaderId = uuidSchema.safeParse(headerDeviceId);

  if (
    !secret ||
    !parsedRouteId.success ||
    !parsedHeaderId.success ||
    parsedRouteId.data !== parsedHeaderId.data
  ) {
    throw new HttpError(401, "Unauthorized.");
  }

  const device = await prisma.device.findUnique({ where: { id: parsedRouteId.data } });
  if (
    !device ||
    device.isRevoked ||
    !(await verifyDeviceSecret(secret, device.deviceSecretHash))
  ) {
    throw new HttpError(401, "Unauthorized.");
  }

  return device;
}
