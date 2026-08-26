import { createHash, randomUUID } from "node:crypto";
import { mkdir, lstat, open, realpath, unlink } from "node:fs/promises";
import path from "node:path";

export type AcceptedImageType = "image/png" | "image/jpeg";

const extensions: Record<AcceptedImageType, string> = {
  "image/png": ".png",
  "image/jpeg": ".jpg",
};

export function validateImageBytes(
  bytes: Buffer,
  mimeType: string,
  maximumBytes: number,
): asserts mimeType is AcceptedImageType {
  if (!(mimeType in extensions)) {
    throw new Error("Unsupported screenshot MIME type.");
  }
  if (bytes.length === 0 || bytes.length > maximumBytes) {
    throw new Error("Screenshot size is outside the allowed range.");
  }
  const isPng = bytes.length >= 8 && bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
  const isJpeg = bytes.length >= 4 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes.at(-2) === 0xff && bytes.at(-1) === 0xd9;
  if ((mimeType === "image/png" && !isPng) || (mimeType === "image/jpeg" && !isJpeg)) {
    throw new Error("Screenshot content does not match its MIME type.");
  }
}

export function resolveStoragePath(storageRoot: string, storageKey: string): string {
  if (!storageKey || path.isAbsolute(storageKey) || storageKey.includes("\0")) {
    throw new Error("Unsafe screenshot storage key.");
  }
  const root = path.resolve(storageRoot);
  const target = path.resolve(root, ...storageKey.split("/"));
  const relative = path.relative(root, target);
  if (relative.startsWith("..") || path.isAbsolute(relative) || relative === "") {
    throw new Error("Screenshot storage key escapes the configured root.");
  }
  return target;
}

async function assertRealPathInside(root: string, candidate: string): Promise<void> {
  const realRoot = await realpath(root);
  const realCandidate = await realpath(candidate);
  const relative = path.relative(realRoot, realCandidate);
  if (relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new Error("Screenshot storage path escapes through a filesystem link.");
  }
}

export class LocalFilesystemScreenshotStorage {
  constructor(private readonly storageRoot: string) {}

  async store(
    deviceId: string,
    capturedAt: Date,
    bytes: Buffer,
    mimeType: AcceptedImageType,
  ): Promise<{ storageKey: string; sizeBytes: number; sha256: string }> {
    const year = capturedAt.getUTCFullYear().toString().padStart(4, "0");
    const month = (capturedAt.getUTCMonth() + 1).toString().padStart(2, "0");
    const storageKey = `${year}/${month}/${deviceId}/${randomUUID()}${extensions[mimeType]}`;
    const target = resolveStoragePath(this.storageRoot, storageKey);
    const parent = path.dirname(target);
    await mkdir(parent, { recursive: true });
    await assertRealPathInside(this.storageRoot, parent);

    const handle = await open(target, "wx", 0o600);
    try {
      await handle.writeFile(bytes);
    } finally {
      await handle.close();
    }
    return {
      storageKey,
      sizeBytes: bytes.length,
      sha256: createHash("sha256").update(bytes).digest("hex"),
    };
  }

  async delete(storageKey: string): Promise<"deleted" | "missing"> {
    const target = resolveStoragePath(this.storageRoot, storageKey);
    try {
      const stats = await lstat(target);
      if (stats.isSymbolicLink()) {
        throw new Error("Refusing to follow a screenshot filesystem link.");
      }
      await assertRealPathInside(this.storageRoot, target);
      await unlink(target);
      return "deleted";
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        return "missing";
      }
      throw error;
    }
  }

  async read(storageKey: string): Promise<string> {
    const target = resolveStoragePath(this.storageRoot, storageKey);
    const stats = await lstat(target);
    if (stats.isSymbolicLink()) {
      throw new Error("Refusing to follow a screenshot filesystem link.");
    }
    await assertRealPathInside(this.storageRoot, target);
    return target;
  }
}
