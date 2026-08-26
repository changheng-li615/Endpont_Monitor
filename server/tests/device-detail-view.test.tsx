import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ScreenshotGallery, screenshotRoute } from "@/components/screenshot-gallery";
import {
  buildDeviceDetailHref,
  createPagination,
  formatAgentVersion,
  formatWindowsVersion,
  groupCurrentProcesses,
  paginate,
  paginationItems,
  PROCESS_PAGE_SIZES,
} from "@/lib/device-detail-view";

describe("device summary formatting", () => {
  it("shows a normal semantic Agent version without invented build metadata", () => {
    expect(formatAgentVersion("1.2.3")).toEqual({ primary: "1.2.3", secondary: null, raw: "1.2.3" });
  });

  it("separates and visually truncates long Agent build metadata while retaining the raw value", () => {
    const raw = "1.0.0+eb286683eff300af1a39d5773f788538aa22a6bf";
    expect(formatAgentVersion(raw)).toEqual({ primary: "1.0.0", secondary: "Build eb28668...", raw });
  });

  it("handles missing and unusually long non-semantic values without returning an unbounded label", () => {
    expect(formatAgentVersion(null).primary).toBe("Unknown");
    const formatted = formatAgentVersion("a".repeat(100));
    expect(formatted.primary.length).toBeLessThan(100);
    expect(formatted.raw).toHaveLength(100);
  });

  it("uses an explicit friendly Windows name and does not guess one from an NT build", () => {
    expect(formatWindowsVersion("Microsoft Windows 11 Pro 10.0.22631.3155")).toMatchObject({
      primary: "Windows 11 Pro",
      secondary: "Build 22631.3155",
    });
    expect(formatWindowsVersion("Microsoft Windows NT 10.0.26200.0")).toMatchObject({
      primary: "Windows",
      secondary: "Build 26200.0",
    });
  });
});

describe("current process grouping", () => {
  const observedEarly = new Date("2026-08-26T03:00:00.000Z");
  const observedLate = new Date("2026-08-26T03:01:00.000Z");
  const row = {
    id: "one",
    processName: "code.exe",
    pid: 100,
    executablePath: "C:\\Apps\\Code.exe",
    productVersion: null,
    workingSetMb: 10.25,
    isForeground: false,
    observedAt: observedEarly,
  };

  it("groups normalized executable identities and aggregates count, memory, foreground, and latest observation", () => {
    const groups = groupCurrentProcesses([
      row,
      {
        ...row,
        id: "two",
        pid: 101,
        executablePath: "c:/apps/CODE.exe",
        workingSetMb: 20.5,
        isForeground: true,
        observedAt: observedLate,
      },
    ]);
    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({
      displayName: "Code",
      instanceCount: 2,
      category: "Application",
      totalMemoryMb: 30.75,
      isForeground: true,
      observedAt: observedLate,
    });
  });

  it("does not group different executable paths even when process names match", () => {
    const groups = groupCurrentProcesses([
      row,
      { ...row, id: "two", pid: 101, executablePath: "D:\\Portable\\Code.exe" },
    ]);
    expect(groups).toHaveLength(2);
  });
});

describe("shareable pagination", () => {
  const values = Array.from({ length: 60 }, (_, index) => index + 1);

  it("returns first, middle, and final pages", () => {
    expect(paginate(values, createPagination("1", "25", values.length, PROCESS_PAGE_SIZES, 25))).toEqual(values.slice(0, 25));
    expect(paginate(values, createPagination("2", "25", values.length, PROCESS_PAGE_SIZES, 25))).toEqual(values.slice(25, 50));
    expect(paginate(values, createPagination("3", "25", values.length, PROCESS_PAGE_SIZES, 25))).toEqual(values.slice(50));
  });

  it("clamps invalid, negative, and beyond-range pages", () => {
    expect(createPagination("invalid", undefined, 60, PROCESS_PAGE_SIZES, 25).page).toBe(1);
    expect(createPagination("-4", undefined, 60, PROCESS_PAGE_SIZES, 25).page).toBe(1);
    expect(createPagination("99", undefined, 60, PROCESS_PAGE_SIZES, 25).page).toBe(3);
  });

  it("creates compact page links and preserves independent category/filter state", () => {
    expect(paginationItems(5, 10)).toEqual([1, "ellipsis", 4, 5, 6, "ellipsis", 10]);
    const href = buildDeviceDetailHref("device-id", {
      processCategory: "applications",
      eventType: "stop",
      screenshotPage: "2",
    }, { processPage: 3 });
    expect(href).toContain("processCategory=applications");
    expect(href).toContain("processPage=3");
    expect(href).toContain("eventType=stop");
    expect(href).toContain("screenshotPage=2");
  });
});

describe("secure screenshot gallery", () => {
  it("renders only authenticated screenshot routes and never a storage path", () => {
    const html = renderToStaticMarkup(<ScreenshotGallery deviceName="X-02" screenshots={[
      { id: "shot-one", capturedAt: "2026-08-26T03:00:00.000Z", capturedLabel: "26/08/2026, 1:00:00 pm", monitorIndex: 1, width: 1920, height: 1080 },
      { id: "shot-two", capturedAt: "2026-08-26T03:00:00.000Z", capturedLabel: "26/08/2026, 1:00:00 pm", monitorIndex: 2, width: null, height: null },
    ]} />);
    expect(screenshotRoute("shot-one")).toBe("/api/admin/screenshots/shot-one");
    expect(html).toContain("/api/admin/screenshots/shot-one");
    expect(html).toContain("/api/admin/screenshots/shot-two");
    expect(html).not.toContain("storageKey");
    expect(html).not.toContain("runtime\\screenshots");
  });
});
