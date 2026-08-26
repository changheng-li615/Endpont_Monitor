export function SourceBadge({ source }: { source: "Xugar" | "ActivTrak" }) {
  return <span className={`source source-${source.toLowerCase()}`}>Source: {source}</span>;
}
