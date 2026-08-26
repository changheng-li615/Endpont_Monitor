import { mkdir, readFile, rm } from "node:fs/promises";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { cleanupScreenshotRetention } from "@/lib/retention";
import { LocalFilesystemScreenshotStorage, resolveStoragePath, validateImageBytes } from "@/lib/screenshot-storage";

const testRoot = path.resolve("runtime", "storage-unit-tests");
const png = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 0]);
const jpeg = Buffer.from([0xff, 0xd8, 0, 0, 0xff, 0xd9]);

afterEach(async () => {
  await rm(testRoot, { recursive: true, force: true });
});

describe("screenshot validation and storage", () => {
  it("accepts matching PNG and JPEG signatures", () => {
    expect(() => validateImageBytes(png, "image/png", 100)).not.toThrow();
    expect(() => validateImageBytes(jpeg, "image/jpeg", 100)).not.toThrow();
  });

  it("rejects invalid MIME, mismatched content, and oversized images", () => {
    expect(() => validateImageBytes(png, "image/gif", 100)).toThrow(/MIME/);
    expect(() => validateImageBytes(png, "image/jpeg", 100)).toThrow(/match/);
    expect(() => validateImageBytes(png, "image/png", 4)).toThrow(/size/);
  });

  it("generates a confined storage key, preserves bytes, and hashes server-side", async () => {
    await mkdir(testRoot, { recursive: true });
    const storage = new LocalFilesystemScreenshotStorage(testRoot);
    const stored = await storage.store("a9bd35e7-ec54-4ca1-ac36-cce0f604bca9", new Date("2026-08-25T00:00:00Z"), png, "image/png");
    expect(stored.storageKey).toMatch(/^2026\/08\/a9bd35e7-ec54-4ca1-ac36-cce0f604bca9\/[0-9a-f-]+\.png$/);
    expect(stored.sha256).toMatch(/^[0-9a-f]{64}$/);
    expect(await readFile(resolveStoragePath(testRoot, stored.storageKey))).toEqual(png);
  });

  it("rejects absolute and traversal storage keys", () => {
    expect(() => resolveStoragePath(testRoot, "../outside.png")).toThrow(/escapes/);
    expect(() => resolveStoragePath(testRoot, "C:\\outside.png")).toThrow(/Unsafe/);
    expect(() => resolveStoragePath(testRoot, "/outside.png")).toThrow(/Unsafe/);
  });
});

describe("retention coordination", () => {
  it("removes metadata after a file is deleted or already missing", async () => {
    const metadata: string[] = [];
    const result = await cleanupScreenshotRetention(
      { findExpired: async () => [{ id: "one", storageKey: "a.png" }, { id: "two", storageKey: "b.png" }], deleteMetadata: async (id) => { metadata.push(id); } },
      { delete: async (key) => key === "a.png" ? "deleted" : "missing" },
      new Date(),
    );
    expect(result).toEqual({ deleted: 2, failed: 0 });
    expect(metadata).toEqual(["one", "two"]);
  });

  it("keeps metadata when storage deletion fails", async () => {
    let metadataDeleted = false;
    const result = await cleanupScreenshotRetention(
      { findExpired: async () => [{ id: "one", storageKey: "unsafe" }], deleteMetadata: async () => { metadataDeleted = true; } },
      { delete: async () => { throw new Error("denied"); } },
      new Date(),
    );
    expect(result).toEqual({ deleted: 0, failed: 1 });
    expect(metadataDeleted).toBe(false);
  });
});
