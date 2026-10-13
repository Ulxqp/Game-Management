import type { Mode } from "./types";

export interface HistorySegment { started: string; ended: string; }
export interface HistorySession {
  mode: Mode; started: string; finished: string; seconds: number; cycles: number;
  applications: string; segments: HistorySegment[];
}

function valueFrom(block: string, key: string) { return block.match(new RegExp(`^${key}:\\s*(.+)$`, "mi"))?.[1]?.trim() || ""; }
export function historyDate(value: string) { const parsed = new Date(value.includes("T") ? value : value.replace(" ", "T")); return Number.isFinite(parsed.getTime()) ? parsed : null; }

function legacyDurationSeconds(text: string) {
  const main = text.split("(")[0]; let total = 0; let found = false;
  for (const match of main.matchAll(/([\d.]+)\s*(hours?|hrs?|hr|minutes?|mins?|min|seconds?|secs?|sec)\b/gi)) {
    const amount = Number(match[1]); const unit = match[2].toLowerCase(); if (!Number.isFinite(amount)) continue;
    found = true; total += unit.startsWith("h") ? amount * 3600 : unit.startsWith("m") ? amount * 60 : amount;
  }
  if (found) return total;
  const decimalHours = text.match(/\(([\d.]+)\s*hours?\)/i); return decimalHours ? Number(decimalHours[1]) * 3600 : 0;
}

export function parseHistory(history: string): HistorySession[] {
  return history.split(/^-{4,}\s*$/m).map(block => {
    const explicitMode = valueFrom(block, "Mode").toLowerCase(); const mode = explicitMode || (valueFrom(block, "Game") ? "game" : valueFrom(block, "Work") ? "work" : "");
    const segmentText = valueFrom(block, "ActiveSegments"); let segments: HistorySegment[] = [];
    if (segmentText) try {
      const parsed = JSON.parse(segmentText) as Array<Record<string, unknown>>;
      segments = parsed.map(item => ({ started: String(item.StartedAt || item.startedAt || ""), ended: String(item.EndedAt || item.endedAt || "") })).filter(item => historyDate(item.started) && historyDate(item.ended));
    } catch { segments = []; }
    const legacy = valueFrom(block, "Total game time") || valueFrom(block, "Total work time");
    return {
      mode, started: valueFrom(block, "Started"), finished: valueFrom(block, "Finished"),
      seconds: Number(valueFrom(block, "ActiveSeconds")) || legacyDurationSeconds(legacy),
      cycles: Number(valueFrom(block, "Timer cycles")) || 0,
      applications: valueFrom(block, "Applications") || valueFrom(block, "Game") || valueFrom(block, "Work"), segments
    };
  }).filter((item): item is HistorySession => item.mode === "work" || item.mode === "game");
}

function intervals(session: HistorySession) {
  const exact = session.segments.map(segment => ({ start: historyDate(segment.started), end: historyDate(segment.ended) })).filter((item): item is { start: Date; end: Date } => Boolean(item.start && item.end && item.end > item.start));
  if (exact.length) return { values: exact, scale: 1 };
  const start = historyDate(session.started); let end = historyDate(session.finished);
  if (!start) return { values: [] as Array<{ start: Date; end: Date }>, scale: 1 };
  if (!end || end <= start) end = new Date(start.getTime() + Math.max(0, session.seconds) * 1000);
  const span = Math.max(0, (end.getTime() - start.getTime()) / 1000); const scale = span > 0 && session.seconds > 0 ? Math.min(1, session.seconds / span) : 1;
  return { values: [{ start, end }], scale };
}

export function secondsInRange(session: HistorySession, rangeStart?: Date | null, rangeEnd?: Date | null) {
  const lower = rangeStart?.getTime() ?? Number.NEGATIVE_INFINITY; const upper = rangeEnd?.getTime() ?? Number.POSITIVE_INFINITY;
  const source = intervals(session); return source.values.reduce((total, interval) => {
    const start = Math.max(lower, interval.start.getTime()); const end = Math.min(upper, interval.end.getTime());
    return total + Math.max(0, (end - start) / 1000) * source.scale;
  }, 0);
}

export function dailySeconds(session: HistorySession, rangeStart?: Date | null, rangeEnd?: Date | null) {
  const lower = rangeStart?.getTime() ?? Number.NEGATIVE_INFINITY; const upper = rangeEnd?.getTime() ?? Number.POSITIVE_INFINITY;
  const source = intervals(session); const buckets = new Map<number, number>();
  for (const interval of source.values) {
    let cursor = Math.max(lower, interval.start.getTime()); const end = Math.min(upper, interval.end.getTime());
    while (cursor < end) {
      const day = new Date(cursor); day.setHours(0, 0, 0, 0); const next = new Date(day); next.setDate(next.getDate() + 1);
      const sliceEnd = Math.min(end, next.getTime()); buckets.set(day.getTime(), (buckets.get(day.getTime()) || 0) + (sliceEnd - cursor) / 1000 * source.scale); cursor = sliceEnd;
    }
  }
  return buckets;
}
