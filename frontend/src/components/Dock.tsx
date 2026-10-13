import type { LucideIcon } from "lucide-react";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { AppWindow, ChartNoAxesCombined, Clock3, House, Settings2 } from "lucide-react";
import { useRef } from "react";
import type { Page } from "../types";
import { cn } from "../lib/utils";
import { MagicDock, MagicDockIcon } from "./magic-dock";

const entries: { page: Page; icon: LucideIcon }[] = [
  { page: "Home", icon: House }, { page: "Applications", icon: AppWindow }, { page: "Timer Settings", icon: Clock3 }, { page: "Reports", icon: ChartNoAxesCombined }, { page: "Options", icon: Settings2 }
];
export function Dock({ page, onChange }: { page: Page; onChange: (page: Page) => void }) {
  const reduced = useReducedMotion();
  const buttons = useRef<Array<HTMLButtonElement | null>>([]);
  const moveFocus = (index: number, key: string) => {
    let next = index;
    if (key === "ArrowRight" || key === "ArrowDown") next = (index + 1) % entries.length;
    else if (key === "ArrowLeft" || key === "ArrowUp") next = (index - 1 + entries.length) % entries.length;
    else if (key === "Home") next = 0;
    else if (key === "End") next = entries.length - 1;
    else return;
    buttons.current[next]?.focus();
  };
  return <nav aria-label="Main navigation"><MagicDock iconSize={36} iconMagnification={reduced ? 36 : 42} iconDistance={90} className="m-0 h-11 gap-1 rounded-[14px] border-[#e2e8f0] bg-white/95 p-1 shadow-sm backdrop-blur-none">
    {entries.map(({ page: item, icon: Icon }, index) => <MagicDockIcon key={item} style={{ width: item === "Timer Settings" ? 112 : 88, height: 36, padding: 0 }}><button ref={element => { buttons.current[index] = element; }} aria-current={page === item ? "page" : undefined} aria-label={item} title={item} onKeyDown={event => { moveFocus(index, event.key); if (["ArrowRight", "ArrowDown", "ArrowLeft", "ArrowUp", "Home", "End"].includes(event.key)) event.preventDefault(); }} onClick={() => onChange(item)} className={cn("relative flex h-9 min-w-[82px] flex-col items-center justify-center gap-0 whitespace-nowrap rounded-[10px] px-2 text-[9px] font-semibold leading-tight text-[#64748b] transition-colors hover:bg-[#f8fafc] hover:text-[#0f172a] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#93c5fd]", page === item && "bg-[#eff6ff] text-[#2563eb]") }>
      <motion.span whileHover={reduced ? undefined : { y: -1 }} transition={{ duration: .16 }}><Icon size={15}/></motion.span><span>{item}</span>
      <AnimatePresence>{page === item && <motion.span layoutId="dock-active" className="absolute inset-x-3 -bottom-1 h-0.5 rounded-full bg-[#2563eb]" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}/>}</AnimatePresence>
    </button></MagicDockIcon>)}
  </MagicDock></nav>;
}
