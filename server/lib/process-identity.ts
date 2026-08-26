import { sha256Hex } from "@/lib/security";

export function createProcessKey(
  pid: number,
  processName: string,
  executablePath: string | null,
): string {
  const identity = executablePath?.trim().replaceAll("/", "\\").toLocaleLowerCase("en-US") ||
    processName.trim().toLocaleLowerCase("en-US");
  return sha256Hex(`${pid}\0${identity}`);
}

export type HumanProcessCategory = "Application" | "System" | "Unknown";

export function categorizeProcessPath(executablePath: string | null): HumanProcessCategory {
  if (!executablePath) {
    return "Unknown";
  }
  const normalized = executablePath.trim().replaceAll("/", "\\").toLocaleLowerCase("en-US");
  if (!/^[a-z]:\\/.test(normalized)) {
    return "Unknown";
  }
  return normalized.startsWith("c:\\windows\\") ? "System" : "Application";
}
