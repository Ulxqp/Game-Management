export type Mode = "work" | "game";
export type Page = "Home" | "Applications" | "Timer Settings" | "Reports" | "Options";

export interface TimerState {
  Mode?: string; Phase?: string; Deadline?: string; Paused?: boolean;
  RemainingSeconds?: number; SessionSeconds?: number; BreakSeconds?: number;
  Seconds?: number; Minutes?: number; Cycle?: number; Status?: string;
}
export interface AppSettings {
  activeMode?: Mode; pollSeconds?: number; automaticModeDetection?: boolean;
  gameTimerEnabled?: boolean; gameTimerSeconds?: number; gameTimerMinutes?: number;
  breakTimerSeconds?: number; breakTimerMinutes?: number;
  workTimerEnabled?: boolean; workTimerSeconds?: number; workTimerMinutes?: number;
  workBreakSeconds?: number; workBreakMinutes?: number;
  timerPopupEnabled?: boolean; timerSoundEnabled?: boolean; sessionSummaryEnabled?: boolean; customAlarmPath?: string;
  gameApps?: string[]; workApps?: string[]; gameFolders?: string[];
  [key: string]: unknown;
}
export interface TaskItem { Id: string; Title: string; Category: "Work" | "Game" | "Personal"; Completed: boolean; CreatedUtc: string; }
export interface PanelPreferences { Version?: number; CalendarPinned: boolean; TasksPinned: boolean; }
export interface SessionState { Mode?: string; ElapsedSeconds?: number; SessionElapsedSeconds?: number; RunningSince?: string | null; SessionRunningSince?: string | null; Paused?: boolean; SessionId?: string; }
export interface Snapshot {
  serverUtc: string; rootPath: string; watcherRunning: boolean; engineEnabled: boolean;
  settings: AppSettings; runtime: Record<string, unknown> | null;
  timers: Record<Mode, TimerState | null>; counters: Record<string, number>;
  performance: Record<string, unknown> | null; tasks: TaskItem[]; history: string;
  panelPreferences: PanelPreferences; sessions: Record<Mode, SessionState | null>;
}
export interface BridgeCommand { name: "pause" | "resume" | "reset" | "openNative" | "chooseAlarmSound" | "previewAlarmSound"; mode?: Mode; action?: "reset-timer" | "reset-cycles" | "reset-all"; path?: string; }
export interface BridgeResult { ok: boolean; message: string; path?: string; }
