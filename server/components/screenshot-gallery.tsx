"use client";

import Image from "next/image";
import { useEffect, useState } from "react";

export type ScreenshotGalleryItem = {
  id: string;
  capturedAt: string;
  capturedLabel: string;
  monitorIndex: number;
  width: number | null;
  height: number | null;
};

export function screenshotRoute(id: string): string {
  return `/api/admin/screenshots/${encodeURIComponent(id)}`;
}

export function ScreenshotGallery({
  screenshots,
  deviceName,
}: {
  screenshots: ScreenshotGalleryItem[];
  deviceName: string;
}) {
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const selected = selectedIndex === null ? null : screenshots[selectedIndex];

  useEffect(() => {
    if (selectedIndex === null) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setSelectedIndex(null);
      if (event.key === "ArrowLeft") setSelectedIndex((index) => index === null ? null : Math.max(0, index - 1));
      if (event.key === "ArrowRight") setSelectedIndex((index) => index === null ? null : Math.min(screenshots.length - 1, index + 1));
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [screenshots.length, selectedIndex]);

  return (
    <>
      <div className="thumbnail-grid">
        {screenshots.map((shot, index) => (
          <figure key={shot.id}>
            <button
              className="screenshot-thumbnail"
              type="button"
              onClick={() => setSelectedIndex(index)}
              aria-label={`Open ${deviceName} monitor ${shot.monitorIndex} screenshot from ${shot.capturedLabel}`}
            >
              <Image
                unoptimized
                width={320}
                height={180}
                src={screenshotRoute(shot.id)}
                alt={`${deviceName}, monitor ${shot.monitorIndex}`}
              />
            </button>
            <figcaption>Monitor {shot.monitorIndex} &middot; {shot.capturedLabel}</figcaption>
          </figure>
        ))}
      </div>
      {selected ? (
        <div className="lightbox" role="dialog" aria-modal="true" aria-label={`${deviceName} screenshot viewer`} onMouseDown={() => setSelectedIndex(null)}>
          <div className="lightbox-window" onMouseDown={(event) => event.stopPropagation()}>
            <div className="lightbox-header">
              <div>
                <strong>{deviceName} screenshot</strong>
                <small>Monitor {selected.monitorIndex} &middot; {selected.capturedLabel}</small>
              </div>
              <button className="lightbox-close" type="button" onClick={() => setSelectedIndex(null)} aria-label="Close screenshot viewer">Close</button>
            </div>
            <div className="lightbox-image-wrap">
              <Image
                unoptimized
                className="lightbox-image"
                width={selected.width && selected.width > 0 ? selected.width : 1920}
                height={selected.height && selected.height > 0 ? selected.height : 1080}
                src={screenshotRoute(selected.id)}
                alt={`${deviceName}, monitor ${selected.monitorIndex}, captured ${selected.capturedLabel}`}
              />
            </div>
            <div className="lightbox-actions">
              <button type="button" disabled={selectedIndex === 0} onClick={() => setSelectedIndex((index) => index === null ? null : Math.max(0, index - 1))}>Previous</button>
              <a href={screenshotRoute(selected.id)} target="_blank" rel="noreferrer">Open original</a>
              <button type="button" disabled={selectedIndex === screenshots.length - 1} onClick={() => setSelectedIndex((index) => index === null ? null : Math.min(screenshots.length - 1, index + 1))}>Next</button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
