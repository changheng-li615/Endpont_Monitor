import { authenticateDevice } from "@/lib/device-auth";
import { getRuntimeEnvironment } from "@/lib/environment";
import { HttpError, routeError } from "@/lib/http";
import { prisma } from "@/lib/prisma";
import { screenshotMetadataSchema } from "@/lib/schemas";
import { LocalFilesystemScreenshotStorage, validateImageBytes } from "@/lib/screenshot-storage";
import { sha256Hex } from "@/lib/security";

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
      captureId: form.get("captureId") || null,
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
    const contentHash = sha256Hex(bytes);
    if (metadata.data.captureId) {
      const existing = await prisma.screenshot.findUnique({
        where: {
          deviceId_captureId: {
            deviceId: device.id,
            captureId: metadata.data.captureId,
          },
        },
      });
      if (existing) {
        if (!matchesExistingCapture(existing, metadata.data, contentHash, file.type)) {
          throw new HttpError(409, "Screenshot capture ID already exists with different content.");
        }
        return Response.json(
          { screenshotId: existing.id, sha256: existing.sha256, duplicate: true },
          { status: 200 },
        );
      }
    }
    const storage = new LocalFilesystemScreenshotStorage(environment.XUGAR_SCREENSHOT_STORAGE_ROOT);
    const stored = await storage.store(device.id, capturedAt, bytes, file.type);
    try {
      const screenshot = await prisma.screenshot.create({
        data: {
          deviceId: device.id,
          captureId: metadata.data.captureId ?? null,
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
      if (metadata.data.captureId) {
        const existing = await prisma.screenshot.findUnique({
          where: {
            deviceId_captureId: {
              deviceId: device.id,
              captureId: metadata.data.captureId,
            },
          },
        });
        if (existing && matchesExistingCapture(existing, metadata.data, stored.sha256, file.type)) {
          return Response.json(
            { screenshotId: existing.id, sha256: existing.sha256, duplicate: true },
            { status: 200 },
          );
        }
      }
      throw error;
    }
  } catch (error) {
    return routeError(error);
  }
}

function matchesExistingCapture(
  existing: {
    capturedAt: Date;
    monitorIndex: number;
    width: number | null;
    height: number | null;
    mimeType: string;
    sha256: string;
  },
  metadata: {
    capturedAt: string;
    monitorIndex: number;
    width?: number | null;
    height?: number | null;
  },
  sha256: string,
  mimeType: string,
): boolean {
  return existing.sha256 === sha256 &&
    existing.mimeType === mimeType &&
    existing.capturedAt.getTime() === new Date(metadata.capturedAt).getTime() &&
    existing.monitorIndex === metadata.monitorIndex &&
    existing.width === (metadata.width ?? null) &&
    existing.height === (metadata.height ?? null);
}
