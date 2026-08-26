import { categorizeProcessPath, type HumanProcessCategory } from "@/lib/process-identity";

export const PROCESS_PAGE_SIZES = [25, 50, 100] as const;
export const EVENT_PAGE_SIZES = [25, 50, 100] as const;
export const SCREENSHOT_PAGE_SIZES = [12, 20, 48] as const;

export type QueryValues = Record<string, string | string[] | undefined>;
export type ProcessCategoryFilter = "applications" | "systems" | "all";
export type EventTypeFilter = "all" | "start" | "stop";

export type FormattedDeviceValue = {
  primary: string;
  secondary: string | null;
  raw: string | null;
};

export type CurrentProcessInput = {
  id: string;
  processName: string;
  pid: number;
  executablePath: string | null;
  productVersion: string | null;
  workingSetMb: number | null;
  isForeground: boolean;
  observedAt: Date;
};

export type GroupedCurrentProcess = {
  identity: string;
  processName: string;
  displayName: string;
  instanceCount: number;
  category: HumanProcessCategory;
  totalMemoryMb: number | null;
  isForeground: boolean;
  executablePath: string | null;
  observedAt: Date;
};

export type Pagination = {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  skip: number;
  firstItem: number;
  lastItem: number;
};

function ellipsize(value: string, maximumLength: number): string {
  if (value.length <= maximumLength) return value;
  return `${value.slice(0, Math.max(1, maximumLength))}...`;
}

export function formatAgentVersion(value: string | null | undefined): FormattedDeviceValue {
  const raw = value?.trim() || null;
  if (!raw) return { primary: "Unknown", secondary: null, raw };

  const semantic = raw.match(/^(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)(?:\+(.+))?$/);
  if (semantic) {
    return {
      primary: semantic[1],
      secondary: semantic[2] ? `Build ${ellipsize(semantic[2], 7)}` : null,
      raw,
    };
  }

  return { primary: ellipsize(raw, 28), secondary: null, raw };
}

export function formatWindowsVersion(value: string | null | undefined): FormattedDeviceValue {
  const raw = value?.trim() || null;
  if (!raw) return { primary: "Windows", secondary: null, raw };

  const friendlyName = raw.match(/\bWindows\s+(?:10|11)(?:\s+(?:Home|Pro|Enterprise|Education|SE|IoT Enterprise))?/i)?.[0];
  const version = raw.match(/\b(?:NT\s+)?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?\b/i);
  const build = version ? [version[3], version[4]].filter(Boolean).join(".") : null;

  if (friendlyName) {
    return {
      primary: friendlyName.replace(/^windows/i, "Windows"),
      secondary: build ? `Build ${build}` : null,
      raw,
    };
  }

  if (/\bWindows\b/i.test(raw)) {
    return { primary: "Windows", secondary: build ? `Build ${build}` : null, raw };
  }

  return { primary: ellipsize(raw, 28), secondary: null, raw };
}

function normalizeExecutablePath(value: string | null): string | null {
  const normalized = value?.trim().replaceAll("/", "\\").toLocaleLowerCase("en-US");
  return normalized || null;
}

function normalizeProcessName(value: string): string {
  return value.trim().toLocaleLowerCase("en-US");
}

function formatProcessName(value: string): string {
  const withoutExtension = value.trim().replace(/\.exe$/i, "") || "Unknown process";
  if (withoutExtension === withoutExtension.toLocaleLowerCase("en-US")) {
    return `${withoutExtension.charAt(0).toLocaleUpperCase("en-US")}${withoutExtension.slice(1)}`;
  }
  return withoutExtension;
}

export function groupCurrentProcesses(rows: CurrentProcessInput[]): GroupedCurrentProcess[] {
  const groups = new Map<string, CurrentProcessInput[]>();

  for (const row of rows) {
    const normalizedPath = normalizeExecutablePath(row.executablePath);
    const normalizedName = normalizeProcessName(row.processName);
    const identity = normalizedPath ? `path:${normalizedPath}` : `name:${normalizedName}`;
    const group = groups.get(identity);
    if (group) group.push(row);
    else groups.set(identity, [row]);
  }

  return [...groups.entries()]
    .map(([identity, instances]) => {
      const paths = [...new Map(
        instances
          .filter((instance) => instance.executablePath?.trim())
          .map((instance) => [normalizeExecutablePath(instance.executablePath), instance.executablePath!.trim()]),
      ).values()];
      const processNames = [...instances.map((instance) => instance.processName)].sort((left, right) =>
        left.localeCompare(right, "en-US", { sensitivity: "base" }),
      );
      const memoryValues = instances
        .map((instance) => instance.workingSetMb)
        .filter((memory): memory is number => memory !== null && Number.isFinite(memory));
      const executablePath = paths.length === 1 ? paths[0] : paths.length > 1 ? "Multiple paths" : null;

      return {
        identity,
        processName: processNames[0] ?? "Unknown process",
        displayName: formatProcessName(processNames[0] ?? "Unknown process"),
        instanceCount: instances.length,
        category: categorizeProcessPath(executablePath === "Multiple paths" ? null : executablePath),
        totalMemoryMb: memoryValues.length > 0 ? memoryValues.reduce((total, memory) => total + memory, 0) : null,
        isForeground: instances.some((instance) => instance.isForeground),
        executablePath,
        observedAt: new Date(Math.max(...instances.map((instance) => instance.observedAt.getTime()))),
      } satisfies GroupedCurrentProcess;
    })
    .sort((left, right) =>
      left.displayName.localeCompare(right.displayName, "en-US", { sensitivity: "base" }) ||
      left.identity.localeCompare(right.identity, "en-US"),
    );
}

export function parseProcessCategory(value: string | undefined): ProcessCategoryFilter {
  return value === "applications" || value === "systems" ? value : "all";
}

export function processMatchesCategory(
  process: GroupedCurrentProcess,
  filter: ProcessCategoryFilter,
): boolean {
  if (filter === "applications") return process.category === "Application";
  if (filter === "systems") return process.category === "System";
  return true;
}

export function parseEventType(value: string | undefined): EventTypeFilter {
  return value === "start" || value === "stop" ? value : "all";
}

export function queryValue(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function parsePageSize(value: string | undefined, allowed: readonly number[], fallback: number): number {
  const parsed = Number.parseInt(value ?? "", 10);
  return allowed.includes(parsed) ? parsed : fallback;
}

export function createPagination(
  requestedPage: string | number | undefined,
  requestedPageSize: string | undefined,
  totalItems: number,
  allowedPageSizes: readonly number[],
  defaultPageSize: number,
): Pagination {
  const parsedPage = typeof requestedPage === "number" ? requestedPage : Number.parseInt(requestedPage ?? "", 10);
  const pageSize = parsePageSize(requestedPageSize, allowedPageSizes, defaultPageSize);
  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));
  const page = Math.min(Math.max(Number.isFinite(parsedPage) ? parsedPage : 1, 1), totalPages);
  const skip = (page - 1) * pageSize;
  return {
    page,
    pageSize,
    totalItems,
    totalPages,
    skip,
    firstItem: totalItems === 0 ? 0 : skip + 1,
    lastItem: Math.min(skip + pageSize, totalItems),
  };
}

export function paginate<T>(values: T[], pagination: Pagination): T[] {
  return values.slice(pagination.skip, pagination.skip + pagination.pageSize);
}

export function paginationItems(currentPage: number, totalPages: number): Array<number | "ellipsis"> {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, index) => index + 1);
  const pages = new Set([1, totalPages, currentPage - 1, currentPage, currentPage + 1]);
  const validPages = [...pages].filter((page) => page >= 1 && page <= totalPages).sort((a, b) => a - b);
  const result: Array<number | "ellipsis"> = [];
  for (const page of validPages) {
    const previous = result[result.length - 1];
    if (typeof previous === "number" && page - previous > 1) result.push("ellipsis");
    result.push(page);
  }
  return result;
}

export function buildDeviceDetailHref(
  deviceId: string,
  current: QueryValues,
  updates: Record<string, string | number | null>,
): string {
  const query = new URLSearchParams();
  for (const [key, rawValue] of Object.entries(current)) {
    const value = queryValue(rawValue);
    if (value) query.set(key, value);
  }
  for (const [key, value] of Object.entries(updates)) {
    if (value === null || value === "") query.delete(key);
    else query.set(key, String(value));
  }
  query.sort();
  const serialized = query.toString();
  return `/admin/devices/${encodeURIComponent(deviceId)}${serialized ? `?${serialized}` : ""}`;
}
