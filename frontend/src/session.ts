import type { Mode, SessionState } from "./types";

function isMode(state: Record<string, unknown> | SessionState | null | undefined, mode: Mode) {
  return String(state?.Mode || "").toLowerCase() === mode;
}

export function selectSessionState(runtime: Record<string, unknown> | null, preserved: SessionState | null | undefined, mode: Mode): SessionState | null {
  const active = isMode(runtime, mode) ? runtime as SessionState : null;
  const saved = isMode(preserved, mode) ? preserved || null : null;
  if (!active) return saved;
  if (!saved?.Paused) return active;
  const activeId = String(active.SessionId || "");
  const savedId = String(saved.SessionId || "");
  return savedId && savedId === activeId ? saved : active;
}

export function startOfReportingWeek(value: Date) {
  const monday = new Date(value);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  monday.setHours(0, 0, 0, 0);
  return monday;
}
