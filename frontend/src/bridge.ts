import type { AppSettings, BridgeCommand, BridgeResult, PanelPreferences, Snapshot, TaskItem } from "./types";

interface RequestOptions { signal?: AbortSignal; }

async function request<T>(path: string, body?: unknown, options: RequestOptions = {}): Promise<T> {
  const response = await fetch(path, { method: body === undefined ? "GET" : "POST", headers: { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body), cache: "no-store", signal: options.signal });
  if (!response.ok) throw new Error((await response.text()) || `Request failed (${response.status})`);
  return response.json() as Promise<T>;
}

export interface SerializedPollingOptions<T> {
  request(signal: AbortSignal): Promise<T>;
  onValue(value: T): void;
  onError(error: unknown, timedOut: boolean): void;
  onDelayed?(): void;
  intervalMs?: number;
  timeoutMs?: number;
  delayedAfterMs?: number;
}

export interface SerializedPoller {
  requestNow(): Promise<void>;
  stop(): void;
}

export function startSerializedPolling<T>({ request: load, onValue, onError, onDelayed, intervalMs = 1000, timeoutMs = 5000, delayedAfterMs = 2500 }: SerializedPollingOptions<T>): SerializedPoller {
  let stopped = false;
  let queued = false;
  let sequence = 0;
  let lastAppliedSequence = 0;
  let intervalTimer: ReturnType<typeof setTimeout> | undefined;
  let activeController: AbortController | null = null;
  let activePromise: Promise<void> | null = null;

  const clearIntervalTimer = () => { if (intervalTimer !== undefined) { clearTimeout(intervalTimer); intervalTimer = undefined; } };
  const schedule = () => {
    if (stopped) return;
    clearIntervalTimer();
    intervalTimer = setTimeout(() => { intervalTimer = undefined; void run(); }, Math.max(0, intervalMs));
  };
  const run = (): Promise<void> => {
    if (stopped) return Promise.resolve();
    clearIntervalTimer();
    if (activePromise) { queued = true; return activePromise; }

    const requestSequence = ++sequence;
    const controller = new AbortController();
    activeController = controller;
    let timedOut = false;
    const delayedTimer = setTimeout(() => { if (!stopped && activeController === controller) onDelayed?.(); }, Math.max(0, delayedAfterMs));
    const timeoutTimer = setTimeout(() => { timedOut = true; controller.abort(); }, Math.max(1, timeoutMs));

    activePromise = (async () => {
      try {
        const value = await load(controller.signal);
        if (!stopped && requestSequence > lastAppliedSequence) { lastAppliedSequence = requestSequence; onValue(value); }
      } catch (error) {
        if (!stopped) onError(error, timedOut);
      } finally {
        clearTimeout(delayedTimer);
        clearTimeout(timeoutTimer);
        if (activeController === controller) activeController = null;
        activePromise = null;
        if (stopped) return;
        if (queued) { queued = false; void run(); }
        else schedule();
      }
    })();
    return activePromise;
  };

  void run();
  return {
    requestNow: run,
    stop: () => {
      if (stopped) return;
      stopped = true;
      queued = false;
      clearIntervalTimer();
      activeController?.abort();
      activeController = null;
    }
  };
}

export const bridge = {
  snapshot: (options?: RequestOptions) => request<Snapshot>("/api/snapshot", undefined, options),
  command: (command: BridgeCommand) => request<BridgeResult>("/api/command", command),
  saveSettings: (settings: AppSettings) => request<BridgeResult>("/api/settings", settings),
  saveTasks: (tasks: TaskItem[]) => request<BridgeResult>("/api/tasks", tasks),
  savePanelPreferences: (preferences: PanelPreferences) => request<BridgeResult>("/api/panels", preferences),
  windowControl: (action: "drag" | "minimize" | "maximize" | "close") => {
    const host = window as unknown as { chrome?: { webview?: { postMessage(message: string): void } } };
    host.chrome?.webview?.postMessage(`window:${action}`);
  }
};
