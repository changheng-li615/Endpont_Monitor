import { authenticateDevice } from "@/lib/device-auth";
import { getRuntimeEnvironment } from "@/lib/environment";
import { HttpError, routeError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { screenshotMetadataSchema } from "@/lib/schemas";
import { LocalFilesystemScreenshotStorage, validateImageBytes } from "@/lib/screenshot-storage";

type RouteContext = { params: Promise<{ id: string }> };

export const runtime = "nodejs";

export async function POST(request: Request, context: RouteContext): Promise<Response> {
  try {
    const { id } = await context.params;
    const device = await authenticateDevice(request, id);
    const environment = getRuntimeEnvironment();
    const contentLength = Number(request.headers.get("content-length") ?? "0");
    if (Number.isFinite(contentLength) && contentLength > environment.XUGAR_SCREENSHOT_MAX_BYTES + 64 * 1024) {
      throw new HttpError(413, "Screenshot upload is too large.");
    }

    const form = await request.formData();
    const file = form.get("file");
    if (!(file instanceof File)) {
      throw new HttpError(400, "A screenshot file is required.");
    }
    const metadata = screenshotMetadataSchema.safeParse({
      capturedAt: form.get("capturedAt"),
      monitorIndex: form.get("monitorIndex"),
      width: form.get("width") || null,
      height: form.get("height") || null,
    });
    if (!metadata.success) {
      throw new HttpError(400, "Screenshot metadata failed validation.");
    }

    const bytes = Buffer.from(await file.arrayBuffer());
    try {
      validateImageBytes(bytes, file.type, environment.XUGAR_SCREENSHOT_MAX_BYTES);
    } catch {
      throw new HttpError(400, "Screenshot content is invalid or unsupported.");
    }
    const capturedAt = new Date(metadata.data.capturedAt);
    const storage = new LocalFilesystemScreenshotStorage(environment.XUGAR_SCREENSHOT_STORAGE_ROOT);
    const stored = await storage.store(device.id, capturedAt, bytes, file.type);
    try {
      const screenshot = await prisma.screenshot.create({
        data: {
          deviceId: device.id,
          capturedAt,
          monitorIndex: metadata.data.monitorIndex,
          width: metadata.data.width ?? null,
          height: metadata.data.height ?? null,
          mimeType: file.type,
          ...stored,
        },
      });
      await prisma.device.update({ where: { id: device.id }, data: { lastSeenAt: new Date() } });
      return Response.json({ screenshotId: screenshot.id, sha256: screenshot.sha256 }, { status: 201 });
    } catch (error) {
      await storage.delete(stored.storageKey).catch(() => undefined);
      throw error;
    }
  } catch (error) {
    return routeError(error);
  }
}
