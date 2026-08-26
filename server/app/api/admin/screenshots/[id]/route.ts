import { readFile } from "node:fs/promises";
import { getRuntimeEnvironment } from "@/lib/environment";
import { getManagerIdentity } from "@/lib/manager-auth";
import { prisma } from "@/lib/prisma";
import { LocalFilesystemScreenshotStorage } from "@/lib/screenshot-storage";
import { uuidSchema } from "@/lib/schemas";

type RouteContext = { params: Promise<{ id: string }> };

export const runtime = "nodejs";

export async function GET(_request: Request, context: RouteContext): Promise<Response> {
  const actorIdentifier = await getManagerIdentity();
  if (!actorIdentifier) {
    return Response.json({ error: "Unauthorized." }, { status: 401 });
  }
  const { id } = await context.params;
  if (!uuidSchema.safeParse(id).success) {
    return Response.json({ error: "Not found." }, { status: 404 });
  }
  const screenshot = await prisma.screenshot.findUnique({ where: { id } });
  if (!screenshot) {
    return Response.json({ error: "Not found." }, { status: 404 });
  }
  try {
    const environment = getRuntimeEnvironment();
    const storage = new LocalFilesystemScreenshotStorage(environment.XUGAR_SCREENSHOT_STORAGE_ROOT);
    const localPath = await storage.read(screenshot.storageKey);
    const bytes = await readFile(localPath);
    await prisma.auditEvent.create({
      data: {
        actorIdentifier,
        action: "SCREENSHOT_VIEWED",
        targetType: "Screenshot",
        targetId: screenshot.id,
        summary: "Authorized manager viewed an Xugar screenshot.",
      },
    });
    return new Response(bytes, {
      headers: {
        "Content-Type": screenshot.mimeType,
        "Content-Length": bytes.length.toString(),
        "Cache-Control": "private, no-store",
        "Content-Disposition": `inline; filename="xugar-${screenshot.id}.${screenshot.mimeType === "image/png" ? "png" : "jpg"}"`,
        "X-Content-Type-Options": "nosniff",
      },
    });
  } catch {
    return Response.json({ error: "Screenshot is unavailable." }, { status: 404 });
  }
}
