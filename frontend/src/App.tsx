import { addDays } from "date-fns";
import { Gamepad2, Minus, RefreshCw, Square, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import type { DateRange } from "react-day-picker";
import { bridge, startSerializedPolling, type SerializedPoller } from "./bridge";
import { Dashboard } from "./components/Dashboard";
import { Dock } from "./components/Dock";
import { LeftSidebar } from "./components/Overlays";
import { ApplicationsPage, OptionsPage, ReportsPage, TimerSettingsPage } from "./components/Pages";
import { Button } from "./components/ui";
import type { Mode, Page, Snapshot, TaskItem } from "./types";

export default function App() {
  const previewPage = new URLSearchParams(window.location.search).get("page"); const initialPage: Page = (["Home", "Applications", "Timer Settings", "Reports", "Options"] as Page[]).includes(previewPage as Page) ? previewPage as Page : "Home";
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null); const [error, setError] = useState(""); const [connectionDelayed, setConnectionDelayed] = useState(false); const [page, setPage] = useState<Page>(initialPage); const [mode, setMode] = useState<Mode>("work"); const [tasks, setTasks] = useState<TaskItem[]>([]); const [range, setRange] = useState<DateRange>({ from: addDays(new Date(), -6), to: new Date() }); const initialized = useRef(false); const poller = useRef<SerializedPoller | null>(null);
  const refresh = useCallback(() => poller.current?.requestNow() ?? Promise.resolve(), []);
  useEffect(() => {
    const current = startSerializedPolling({
      request: signal => bridge.snapshot({ signal }),
      onValue: next => {
        setSnapshot(next); setTasks(next.tasks || []);
        if (!initialized.current) { initialized.current = true; setMode(next.settings.activeMode || "work"); }
        setError(""); setConnectionDelayed(false);
      },
      onError: (reason, timedOut) => {
        setError(timedOut ? "The desktop bridge took too long to respond." : reason instanceof Error ? reason.message : "The desktop bridge is unavailable.");
        setConnectionDelayed(true);
      },
      onDelayed: () => setConnectionDelayed(true),
      intervalMs: 1000,
      timeoutMs: 5000,
      delayedAfterMs: 2500
    });
    poller.current = current;
    return () => { if (poller.current === current) poller.current = null; current.stop(); };
  }, []);
  useEffect(() => { const handler = (event: KeyboardEvent) => { if (!event.ctrlKey) return; const pages: Page[] = ["Home", "Applications", "Timer Settings", "Reports", "Options"]; const number = Number(event.key); if (number >= 1 && number <= 5) { event.preventDefault(); setPage(pages[number - 1]); } }; window.addEventListener("keydown", handler); return () => window.removeEventListener("keydown", handler); }, []);
  if (!snapshot) return <main className="grid min-h-screen place-items-center bg-[#f8fafc] p-6"><div className="max-w-md text-center"><div className="mx-auto mb-3 grid h-12 w-12 place-items-center rounded-[14px] bg-[#dbeafe] text-[#2563eb]"><Gamepad2/></div><h1 className="text-lg font-bold">Game Management</h1><p className="mt-2 text-sm text-[#64748b]">{error || "Connecting to the native timer backend…"}</p>{error && <Button className="mt-4" onClick={refresh}><RefreshCw size={15}/>Retry</Button>}</div></main>;
  return <div className="h-screen min-h-[600px] overflow-hidden bg-[#f8fafc] text-[#0f172a]">
    <header onPointerDown={event => { if (event.button === 0 && !(event.target as HTMLElement).closest("button,input,select,a")) bridge.windowControl("drag"); }} onDoubleClick={event => { if (!(event.target as HTMLElement).closest("button,input,select,a")) bridge.windowControl("maximize"); }} className="relative z-50 flex h-14 select-none items-center border-b border-[#e2e8f0] bg-white pl-4"><div className="flex items-center gap-2 text-sm font-bold"><span className="grid h-8 w-8 place-items-center rounded-[9px] bg-[#2563eb] text-white"><Gamepad2 size={17}/></span><span>Game Management</span></div><div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2"><Dock page={page} onChange={setPage}/></div><div className="ml-auto flex h-full items-center"><div role="status" title={connectionDelayed ? error : undefined} className={`mr-2 flex items-center gap-2 text-xs ${connectionDelayed ? "text-[#b45309]" : "text-[#64748b]"}`}><span className={`h-2 w-2 rounded-full ${connectionDelayed ? "bg-[#d97706]" : snapshot.watcherRunning ? "bg-[#16a34a]" : "bg-[#94a3b8]"}`}/>{connectionDelayed ? "Connection delayed—retrying" : snapshot.watcherRunning ? "Engine running" : "Engine paused"}</div><WindowButton label="Minimize" onClick={() => bridge.windowControl("minimize")}><Minus size={15}/></WindowButton><WindowButton label="Maximize or restore" onClick={() => bridge.windowControl("maximize")}><Square size={12}/></WindowButton><WindowButton label="Close" danger onClick={() => bridge.windowControl("close")}><X size={15}/></WindowButton></div></header>
    <div className="gm-workspace relative flex h-[calc(100vh-56px)] min-w-0"><LeftSidebar tasks={tasks} setTasks={setTasks} range={range} setRange={setRange} preferences={snapshot.panelPreferences}/><main className="gm-page-content h-full min-w-0 flex-1 overflow-y-auto px-4 pt-4">{page === "Home" && <Dashboard snapshot={snapshot} mode={mode} setMode={setMode} range={range} refresh={refresh} onOpenApplications={() => setPage("Applications")} onOpenTimerSettings={() => setPage("Timer Settings")}/>} {page === "Applications" && <ApplicationsPage snapshot={snapshot} onSaved={refresh}/>} {page === "Timer Settings" && <TimerSettingsPage snapshot={snapshot} onSaved={refresh}/>} {page === "Reports" && <ReportsPage snapshot={snapshot}/>} {page === "Options" && <OptionsPage snapshot={snapshot} onSaved={refresh}/>}</main></div>
  </div>;
}

function WindowButton({ label, danger, onClick, children }: { label: string; danger?: boolean; onClick(): void; children: React.ReactNode }) { return <button type="button" aria-label={label} title={label} onPointerDown={event => event.stopPropagation()} onDoubleClick={event => event.stopPropagation()} onClick={onClick} className={`grid h-full w-11 place-items-center text-[#475569] transition-colors ${danger ? "hover:bg-[#e81123] hover:text-white" : "hover:bg-[#e2e8f0]"}`}>{children}</button>; }
