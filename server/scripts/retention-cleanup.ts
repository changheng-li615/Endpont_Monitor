import { getRuntimeEnvironment } from "../lib/environment";
import { prisma } from "../lib/prisma";
import { cleanupScreenshotRetention } from "../lib/retention";
import { LocalFilesystemScreenshotStorage } from "../lib/screenshot-storage";

const environment = getRuntimeEnvironment();
const cutoff = new Date(
  Date.now() - environment.XUGAR_SCREENSHOT_RETENTION_DAYS * 24 * 60 * 60 * 1000,
);
const storage = new LocalFilesystemScreenshotStorage(
  environment.XUGAR_SCREENSHOT_STORAGE_ROOT,
);

try {
  const result = await cleanupScreenshotRetention(
    {
      findExpired: (date) =>
        prisma.screenshot.findMany({
          where: { capturedAt: { lt: date } },
          select: { id: true, storageKey: true },
          orderBy: { capturedAt: "asc" },
        }),
      deleteMetadata: async (id) => {
        await prisma.screenshot.delete({ where: { id } });
      },
    },
    storage,
    cutoff,
  );
  await prisma.auditEvent.create({
    data: {
      actorIdentifier: "retention-job",
      action: "SCREENSHOT_RETENTION_EXECUTED",
      targetType: "Screenshot",
      summary: `Retention cleanup deleted ${result.deleted} records and encountered ${result.failed} failures.`,
    },
  });
  console.log(`Screenshot retention complete: deleted=${result.deleted}, failed=${result.failed}`);
  if (result.failed > 0) {
    process.exitCode = 1;
  }
} finally {
  await prisma.$disconnect();
}
