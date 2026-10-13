import { AppWindow, Folder } from "lucide-react";
import { useEffect, useState } from "react";

export function AppIcon({ path, kind = "application", className = "h-6 w-6" }: { path: string; kind?: "application" | "folder"; className?: string }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [path]);
  const fallback = kind === "folder" ? <Folder size={14}/> : <AppWindow size={14}/>;
  return <span className={`grid shrink-0 place-items-center overflow-hidden rounded-[7px] border border-[#dbe3ed] bg-white text-[#2563eb] ${className}`}>
    {kind === "application" && !failed
      ? <img src={`/api/app-icon?path=${encodeURIComponent(path)}`} alt="" aria-hidden="true" draggable={false} className="h-[78%] w-[78%] object-contain" onError={() => setFailed(true)}/>
      : fallback}
  </span>;
}
