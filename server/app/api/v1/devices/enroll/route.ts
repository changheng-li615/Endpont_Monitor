import { prisma } from "@/lib/prisma";
import { enrollmentSchema } from "@/lib/schemas";
import { createDeviceSecret, hashDeviceSecret, readBearerToken, secretsEqual } from "@/lib/security";
import { HttpError, readBoundedJson, routeError } from "@/lib/http";

export async function POST(request: Request): Promise<Response> {
  try {
    const configuredToken = process.env.XUGAR_ENROLLMENT_TOKEN;
    const suppliedToken = readBearerToken(request);
    if (!configuredToken || configuredToken.length < 32 || !suppliedToken || !secretsEqual(suppliedToken, configuredToken)) {
      throw new HttpError(401, "Unauthorized.");
    }

    const input = await readBoundedJson(request, enrollmentSchema, 16 * 1024);
    const existing = await prisma.device.findUnique({
      where: { installationId: input.installationId },
    });
    if (existing?.isRevoked) {
      throw new HttpError(403, "Device enrollment is revoked.");
    }

    const deviceSecret = createDeviceSecret();
    const deviceSecretHash = await hashDeviceSecret(deviceSecret);
    const now = new Date();
    const device = existing
      ? await prisma.device.update({
          where: { id: existing.id },
          data: {
            hostname: input.hostname,
            windowsUser: input.windowsUser,
            workEmail: input.workEmail,
            osVersion: input.osVersion,
            agentVersion: input.agentVersion,
            deviceSecretHash,
            lastSeenAt: now,
          },
        })
      : await prisma.device.create({
          data: {
            ...input,
            deviceSecretHash,
            enrolledAt: now,
            lastSeenAt: now,
          },
        });

    await prisma.auditEvent.create({
      data: {
        actorIdentifier: "device-enrollment",
        action: existing ? "DEVICE_REENROLLED" : "DEVICE_ENROLLED",
        targetType: "Device",
        targetId: device.id,
        summary: existing ? "Device secret rotated during authorized re-enrollment." : "Device enrolled.",
      },
    });

    return Response.json(
      { deviceId: device.id, deviceSecret },
      { status: existing ? 200 : 201 },
    );
  } catch (error) {
    return routeError(error);
  }
}
