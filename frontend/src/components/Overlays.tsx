import { format, parseISO, startOfMonth, startOfWeek } from "date-fns";
import { CalendarDays, Check, ListTodo, Pencil, Pin, PinOff, Plus, Trash2, X } from "lucide-react";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { DayPicker, type DateRange } from "react-day-picker";
import "react-day-picker/style.css";
import { bridge } from "../bridge";
import { cn } from "../lib/utils";
import type { PanelPreferences, TaskItem } from "../types";
import { Badge, Button, inputClass } from "./ui";

type PanelName = "calendar" | "tasks";

export function LeftSidebar({ tasks, setTasks, range, setRange, preferences }: { tasks: TaskItem[]; setTasks: (items: TaskItem[]) => void; range: DateRange; setRange: (range: DateRange) => void; preferences: PanelPreferences }) {
  const [calendarPinned, setCalendarPinned] = useState(Boolean(preferences.CalendarPinned));
  const [tasksPinned, setTasksPinned] = useState(Boolean(preferences.TasksPinned));
  const [calendarOpen, setCalendarOpen] = useState(Boolean(preferences.CalendarPinned));
  const [tasksOpen, setTasksOpen] = useState(Boolean(preferences.TasksPinned));
  const [preferenceError, setPreferenceError] = useState("");
  const calendarTimer = useRef<number | null>(null); const tasksTimer = useRef<number | null>(null);
  const calendarTrigger = useRef<HTMLButtonElement>(null); const tasksTrigger = useRef<HTMLButtonElement>(null);
  const calendarPanel = useRef<HTMLElement>(null); const tasksPanel = useRef<HTMLElement>(null);

  const pinned = (name: PanelName) => name === "calendar" ? calendarPinned : tasksPinned;
  const opened = (name: PanelName) => name === "calendar" ? calendarOpen : tasksOpen;
  const setOpened = (name: PanelName, value: boolean) => name === "calendar" ? setCalendarOpen(value) : setTasksOpen(value);
  const timerFor = (name: PanelName) => name === "calendar" ? calendarTimer : tasksTimer;
  const clearClose = (name: PanelName) => { const timer = timerFor(name); if (timer.current !== null) window.clearTimeout(timer.current); timer.current = null; };
  const show = (name: PanelName) => { clearClose(name); setOpened(name, true); };
  const scheduleClose = (name: PanelName) => { clearClose(name); if (pinned(name)) return; timerFor(name).current = window.setTimeout(() => setOpened(name, false), 260); };
  useEffect(() => () => { clearClose("calendar"); clearClose("tasks"); }, []);

  const persistPins = async (nextCalendar: boolean, nextTasks: boolean) => {
    setPreferenceError("");
    try { await bridge.savePanelPreferences({ Version: 1, CalendarPinned: nextCalendar, TasksPinned: nextTasks }); return true; }
    catch (error) { setPreferenceError(error instanceof Error ? error.message : "Panel pins could not be saved."); return false; }
  };
  const togglePin = async (name: PanelName) => {
    const previousCalendar = calendarPinned, previousTasks = tasksPinned;
    const nextCalendar = name === "calendar" ? !calendarPinned : calendarPinned;
    const nextTasks = name === "tasks" ? !tasksPinned : tasksPinned;
    setCalendarPinned(nextCalendar); setTasksPinned(nextTasks); setOpened(name, true);
    if (!await persistPins(nextCalendar, nextTasks)) { setCalendarPinned(previousCalendar); setTasksPinned(previousTasks); }
  };
  const toggleFromTrigger = async (name: PanelName) => {
    if (pinned(name)) { await closePanel(name); return; }
    clearClose(name); setOpened(name, true); await togglePin(name);
  };
  const closePanel = async (name: PanelName) => {
    const previousCalendar = calendarPinned, previousTasks = tasksPinned;
    const nextCalendar = name === "calendar" ? false : calendarPinned;
    const nextTasks = name === "tasks" ? false : tasksPinned;
    if (pinned(name)) { setCalendarPinned(nextCalendar); setTasksPinned(nextTasks); if (!await persistPins(nextCalendar, nextTasks)) { setCalendarPinned(previousCalendar); setTasksPinned(previousTasks); setOpened(name, true); return; } }
    clearClose(name); setOpened(name, false);
  };
  const focusLeaves = (name: PanelName, next: EventTarget | null) => {
    const node = next as Node | null; const trigger = name === "calendar" ? calendarTrigger.current : tasksTrigger.current; const panel = name === "calendar" ? calendarPanel.current : tasksPanel.current;
    if (!node || (!trigger?.contains(node) && !panel?.contains(node))) scheduleClose(name);
  };
  const visibleCount = Number(calendarOpen) + Number(tasksOpen);
  const hasDockedPanel = calendarPinned || tasksPinned;

  return <aside className={cn("gm-overlay-shell relative z-40 flex h-full shrink-0 border-r border-[#e2e8f0] bg-white transition-[width] duration-200", hasDockedPanel ? "w-[332px]" : "w-11")} aria-label="Calendar and tasks">
    <div className="flex h-full w-11 flex-col items-center gap-2 pt-3">
      <PanelTrigger name="calendar" label="Calendar" open={calendarOpen} buttonRef={calendarTrigger} onShow={() => show("calendar")} onLeave={() => scheduleClose("calendar")} onToggle={() => void toggleFromTrigger("calendar")} onBlur={next => focusLeaves("calendar", next)}><CalendarDays size={18}/></PanelTrigger>
      <PanelTrigger name="tasks" label="Tasks" open={tasksOpen} buttonRef={tasksTrigger} onShow={() => show("tasks")} onLeave={() => scheduleClose("tasks")} onToggle={() => void toggleFromTrigger("tasks")} onBlur={next => focusLeaves("tasks", next)}><ListTodo size={18}/></PanelTrigger>
    </div>
    {visibleCount > 0 && <div className={cn("gm-overlay-column pointer-events-none z-50 grid w-[280px] shrink-0 gap-2", hasDockedPanel ? "relative my-3 ml-2 h-[calc(100%_-_24px)]" : "absolute left-[52px] top-3 max-h-[calc(100%_-_24px)]", visibleCount === 2 ? "grid-rows-[minmax(270px,1.08fr)_minmax(238px,.92fr)]" : "grid-rows-[minmax(0,auto)]")}>
      {calendarOpen && <SidebarPanel ref={calendarPanel} title="Calendar" icon={<CalendarDays size={16}/>} pinned={calendarPinned} onPin={() => void togglePin("calendar")} onClose={() => void closePanel("calendar")} onEnter={() => show("calendar")} onLeave={() => scheduleClose("calendar")} onBlur={next => focusLeaves("calendar", next)} className="gm-calendar-tile"><CalendarPanel range={range} setRange={setRange}/></SidebarPanel>}
      {tasksOpen && <SidebarPanel ref={tasksPanel} title="Tasks" icon={<ListTodo size={16}/>} pinned={tasksPinned} onPin={() => void togglePin("tasks")} onClose={() => void closePanel("tasks")} onEnter={() => show("tasks")} onLeave={() => scheduleClose("tasks")} onBlur={next => focusLeaves("tasks", next)} className="gm-tasks-tile"><TasksPanel tasks={tasks} setTasks={setTasks}/></SidebarPanel>}
    </div>}
    {preferenceError && <span className="absolute bottom-3 left-[52px] z-[70] w-[280px] rounded-[9px] border border-[#fecaca] bg-[#fef2f2] px-3 py-2 text-[10px] text-[#b91c1c] shadow-card" role="status">{preferenceError}</span>}
  </aside>;
}

function PanelTrigger({ name, label, open, buttonRef, onShow, onLeave, onToggle, onBlur, children }: { name: PanelName; label: string; open: boolean; buttonRef: React.RefObject<HTMLButtonElement | null>; onShow(): void; onLeave(): void; onToggle(): void; onBlur(next: EventTarget | null): void; children: React.ReactNode }) {
  return <button ref={buttonRef} type="button" aria-label={`${open ? "Hide" : "Show"} ${label}`} aria-controls={`gm-${name}-panel`} aria-expanded={open} onMouseEnter={onShow} onMouseLeave={onLeave} onFocus={onShow} onBlur={event => onBlur(event.relatedTarget)} onClick={onToggle} className={cn("grid h-9 w-9 place-items-center rounded-[10px] text-[#64748b] outline-none transition-colors hover:bg-[#eff6ff] hover:text-[#2563eb] focus-visible:ring-2 focus-visible:ring-[#93c5fd]", open && "bg-[#dbeafe] text-[#2563eb]")}>{children}</button>;
}

const SidebarPanel = React.forwardRef<HTMLElement, { title: string; icon: React.ReactNode; children: React.ReactNode; pinned: boolean; onPin(): void; onClose(): void; onEnter(): void; onLeave(): void; onBlur(next: EventTarget | null): void; className?: string }>(function SidebarPanel({ title, icon, children, pinned, onPin, onClose, onEnter, onLeave, onBlur, className }, ref) {
  return <section ref={ref} id={`gm-${title.toLowerCase()}-panel`} tabIndex={-1} onMouseEnter={onEnter} onMouseLeave={onLeave} onFocus={onEnter} onBlur={event => onBlur(event.relatedTarget)} className={cn("gm-overlay-panel pointer-events-auto flex min-h-0 flex-col overflow-hidden rounded-card border border-[#e2e8f0] bg-white p-3 shadow-card", className)}><header className="mb-2 flex shrink-0 items-center gap-2 text-[13px] font-bold text-[#0f172a]">{icon}<span className="flex-1">{title}</span><button type="button" onClick={onPin} aria-label={pinned ? `Unpin ${title}` : `Pin ${title}`} aria-pressed={pinned} className="grid h-7 w-7 place-items-center rounded-[8px] text-[#64748b] hover:bg-[#f1f5f9] hover:text-[#2563eb]">{pinned ? <PinOff size={14}/> : <Pin size={14}/>}</button><button type="button" onClick={onClose} aria-label={`Close ${title}`} className="grid h-7 w-7 place-items-center rounded-[8px] text-[#64748b] hover:bg-[#f1f5f9] hover:text-[#0f172a]"><X size={14}/></button></header><div className="min-h-0 flex-1">{children}</div></section>;
});

function CalendarPanel({ range, setRange }: { range: DateRange; setRange(range: DateRange): void }) {
  const [custom, setCustom] = useState(false); const today = new Date();
  const preset = (name: string) => { setCustom(false); if (name === "Today") setRange({ from: today, to: today }); else if (name === "This Week") setRange({ from: startOfWeek(today), to: today }); else setRange({ from: startOfMonth(today), to: today }); };
  const setCustomDate = (part: "from" | "to", value: string) => { if (!value) return; const parsed = parseISO(value); if (!Number.isFinite(parsed.getTime()) || parsed > today) return; const next = { ...range, [part]: parsed }; if (next.from && next.to && next.from > next.to) { if (part === "from") next.to = parsed; else next.from = parsed; } setRange(next); };
  return <div className="flex h-full min-h-0 flex-col gap-2"><div className="grid shrink-0 grid-cols-4 gap-1">{["Today", "This Week", "This Month"].map(value => <Button key={value} onClick={() => preset(value)} variant="outline" size="sm" className="h-7 min-w-0 px-1 text-[9px]">{value}</Button>)}<Button onClick={() => setCustom(value => !value)} variant={custom ? "default" : "outline"} size="sm" className="h-7 min-w-0 px-1 text-[9px]">Custom</Button></div>
    {custom && <div className="grid shrink-0 grid-cols-2 gap-1"><input aria-label="Custom range start" type="date" max={format(today, "yyyy-MM-dd")} className={`${inputClass} h-8 px-1 text-[10px]`} value={range.from ? format(range.from, "yyyy-MM-dd") : ""} onChange={event => setCustomDate("from", event.target.value)}/><input aria-label="Custom range end" type="date" max={format(today, "yyyy-MM-dd")} className={`${inputClass} h-8 px-1 text-[10px]`} value={range.to ? format(range.to, "yyyy-MM-dd") : ""} onChange={event => setCustomDate("to", event.target.value)}/></div>}
    <div className="min-h-0 flex-1 overflow-y-auto"><DayPicker mode="range" selected={range} onSelect={value => value && setRange(value)} disabled={{ after: today }} showOutsideDays className="gm-calendar"/></div>
    <div className="shrink-0 rounded-[9px] bg-[#eff6ff] p-2 text-[10px] text-[#1e40af]"><strong>Selected range</strong><div className="mt-0.5 truncate" title={range.from ? `${format(range.from, "MMM d, yyyy")}${range.to ? ` – ${format(range.to, "MMM d, yyyy")}` : ""}` : "Choose a date"}>{range.from ? format(range.from, "MMM d, yyyy") : "Choose a date"}{range.to && ` – ${format(range.to, "MMM d, yyyy")}`}</div></div>
  </div>;
}

function TasksPanel({ tasks, setTasks }: { tasks: TaskItem[]; setTasks(items: TaskItem[]): void }) {
  const [filter, setFilter] = useState("All"); const [title, setTitle] = useState(""); const [category, setCategory] = useState<TaskItem["Category"]>("Work"); const [editing, setEditing] = useState<string | null>(null); const [editTitle, setEditTitle] = useState(""); const [message, setMessage] = useState("");
  const filtered = useMemo(() => tasks.filter(task => filter === "All" || task.Category === filter), [tasks, filter]); const complete = tasks.filter(task => task.Completed).length;
  const commit = (next: TaskItem[]) => { setTasks(next); setMessage(""); void bridge.saveTasks(next).catch(error => setMessage(error instanceof Error ? error.message : "Tasks could not be saved.")); };
  const add = () => { const clean = title.trim(); if (!clean) { setMessage("Enter a task name first."); return; } commit([...tasks, { Id: crypto.randomUUID().replaceAll("-", ""), Title: clean, Category: category, Completed: false, CreatedUtc: new Date().toISOString() }]); setTitle(""); };
  return <div className="grid h-full min-h-0 grid-rows-[auto_auto_auto_minmax(0,1fr)_auto_auto] gap-2"><div className="grid grid-cols-4 gap-1">{["All", "Work", "Game", "Personal"].map(value => <Button key={value} size="sm" className="h-7 min-w-0 px-1 text-[9px]" variant={filter === value ? (value === "Game" ? "game" : "default") : "outline"} onClick={() => setFilter(value)}>{value}</Button>)}</div>
    <div className="flex gap-1"><input className={`${inputClass} h-8 min-w-0 px-2 text-xs`} value={title} onChange={event => setTitle(event.target.value)} onKeyDown={event => event.key === "Enter" && add()} placeholder="Add a task"/><Button className="h-8 w-8 shrink-0" size="icon" onClick={add}><Plus size={15}/></Button></div>
    <select className={`${inputClass} h-8 px-2 text-xs`} value={category} onChange={event => setCategory(event.target.value as TaskItem["Category"])}><option>Work</option><option>Game</option><option>Personal</option></select>
    <div className="grid min-h-0 content-start gap-1 overflow-y-auto">{filtered.length ? filtered.map(task => <div className="group flex min-h-8 items-center gap-1.5 rounded-[8px] px-1 hover:bg-[#f8fafc]" key={task.Id}><button aria-label={task.Completed ? "Mark incomplete" : "Mark complete"} onClick={() => commit(tasks.map(value => value.Id === task.Id ? { ...value, Completed: !value.Completed } : value))} className={cn("grid h-4 w-4 shrink-0 place-items-center rounded border border-[#cbd5e1]", task.Completed && "border-[#16a34a] bg-[#16a34a] text-white")}>{task.Completed && <Check size={11}/>}</button>{editing === task.Id ? <input autoFocus className={`${inputClass} h-7 min-w-0 flex-1`} value={editTitle} onChange={event => setEditTitle(event.target.value)} onBlur={() => { if (editTitle.trim()) commit(tasks.map(value => value.Id === task.Id ? { ...value, Title: editTitle.trim() } : value)); setEditing(null); }} onKeyDown={event => { if (event.key === "Enter") event.currentTarget.blur(); if (event.key === "Escape") setEditing(null); }}/>:<span className={cn("min-w-0 flex-1 truncate text-[11px]", task.Completed && "text-[#94a3b8] line-through")}>{task.Title}</span>}<Badge className={cn("h-5 px-1.5 text-[9px]", task.Category === "Work" ? "bg-[#dbeafe] text-[#1d4ed8]" : task.Category === "Game" ? "bg-[#fee2e2] text-[#dc2626]" : "")}>{task.Category}</Badge><button aria-label="Edit task" onClick={() => { setEditing(task.Id); setEditTitle(task.Title); }} className="opacity-0 group-focus-within:opacity-100 group-hover:opacity-100"><Pencil size={12} className="text-[#64748b]"/></button><button aria-label="Delete task" onClick={() => commit(tasks.filter(value => value.Id !== task.Id))} className="opacity-0 group-focus-within:opacity-100 group-hover:opacity-100"><Trash2 size={12} className="text-[#94a3b8]"/></button></div>) : <div className="grid h-full min-h-16 place-items-center rounded-[8px] border border-dashed border-[#e2e8f0] px-3 text-center text-[10px] leading-4 text-[#94a3b8]">No tasks in this category</div>}</div>
    <div className="text-xs text-[#64748b]"><div className="mb-1 flex justify-between"><span>{complete} of {tasks.length} complete</span><span>{tasks.length ? Math.round(complete / tasks.length * 100) : 0}%</span></div><div className="h-2 overflow-hidden rounded-full bg-[#e2e8f0]"><div className="h-full rounded-full bg-[#16a34a]" style={{ width: `${tasks.length ? complete / tasks.length * 100 : 0}%` }}/></div></div>
    <span role="status" className="min-h-4 text-[10px] text-[#dc2626]">{message}</span>
  </div>;
}
