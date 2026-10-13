export function advanceServerClock(serverUtc: string, receivedClientMs: number, currentClientMs: number) {
  const serverMs = Date.parse(serverUtc);
  if (!Number.isFinite(serverMs)) return currentClientMs;
  return serverMs + Math.max(0, currentClientMs - receivedClientMs);
}

export function secondsUntil(deadline: string, serverNowMs: number, fallbackSeconds = 0) {
  const deadlineMs = Date.parse(deadline);
  return Number.isFinite(deadlineMs) && Number.isFinite(serverNowMs)
    ? Math.max(0, (deadlineMs - serverNowMs) / 1000)
    : fallbackSeconds;
}

export function countdownTick(deadline: string | undefined, serverNowMs: number) {
  const deadlineMs = deadline ? Date.parse(deadline) : Number.NaN;
  return Number.isFinite(deadlineMs)
    ? Math.max(0, Math.ceil((deadlineMs - serverNowMs) / 1000))
    : Math.floor(serverNowMs / 1000);
}
