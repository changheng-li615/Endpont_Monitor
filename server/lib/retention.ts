export interface RetentionScreenshot {
  id: string;
  storageKey: string;
}

export interface RetentionRepository {
  findExpired(cutoff: Date): Promise<RetentionScreenshot[]>;
  deleteMetadata(id: string): Promise<void>;
}

export interface RetentionStorage {
  delete(storageKey: string): Promise<"deleted" | "missing">;
}

export async function cleanupScreenshotRetention(
  repository: RetentionRepository,
  storage: RetentionStorage,
  cutoff: Date,
): Promise<{ deleted: number; failed: number }> {
  const expired = await repository.findExpired(cutoff);
  let deleted = 0;
  let failed = 0;
  for (const screenshot of expired) {
    try {
      await storage.delete(screenshot.storageKey);
      await repository.deleteMetadata(screenshot.id);
      deleted += 1;
    } catch {
      failed += 1;
    }
  }
  return { deleted, failed };
}
