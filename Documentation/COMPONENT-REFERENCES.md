# React component source audit

Reviewed on October 12, 2026. Game Management now uses a React 19 + TypeScript interface hosted in WebView2. The existing .NET Framework/PowerShell engine remains authoritative, and the native WinForms interface remains a working fallback.

## Integrated free sources

### Magic UI Dock

- Official documentation: <https://magicui.design/docs/components/dock>
- Registry source: <https://magicui.design/r/dock.json>
- License: MIT.
- Integration: the retrieved source is retained in `frontend/src/components/magic-dock.tsx`, with only the local `cn` import and compact top-dock styling adapted for Game Management.
- Result: five real destinations, restrained magnification, active underline, native tooltips, Ctrl+1 through Ctrl+5 shortcuts, and Arrow/Home/End keyboard focus movement.

### shadcn/ui Card, Alert Dialog, and Calendar

- Card: <https://ui.shadcn.com/docs/components/card>
- Alert Dialog: <https://ui.shadcn.com/docs/components/alert-dialog>
- Calendar: <https://ui.shadcn.com/docs/components/calendar>
- License: MIT.
- Integration: official source patterns are adapted in `frontend/src/components/ui.tsx`, `frontend/src/components/Calendar.tsx`, and the consuming pages. The alert uses the official Radix primitive. The calendar uses React DayPicker and date-fns.
- Result: consistent cards, destructive confirmations, Today/This Week/This Month/Custom ranges, week-to-date and month-to-date semantics, and future-date blocking.

### bundui/shadcn-ui-kit task pattern

- Free repository: <https://github.com/bundui/shadcn-ui-kit>
- License: MIT.
- The public repository and its free todo example were verified. The exact example source was not imported because a stable direct component source was not available during retrieval. No inaccessible or paid source is claimed as copied.
- Integration: the task panel is a local implementation using the verified interaction pattern: add, edit, complete, delete, filter, progress, and persisted Version 1 task data.

### TanStack Table and Recharts

- TanStack Table: <https://tanstack.com/table/latest>
- Recharts: <https://recharts.github.io/en-US/>
- Licenses: MIT.
- Integration: Applications uses real filtering and sortable headers; Reports uses real saved history with Daily, Weekly, Monthly, and All Time periods. No demonstration data is used in production.

## Local components and adaptation boundary

- `frontend/src/components/Dashboard.tsx` is project-specific. It displays authoritative timer state, active time, completed cycles, application assignments, and current/peak CPU/GPU temperature when the native sensor service provides it.
- `frontend/src/components/Overlays.tsx` is project-specific. It implements the fixed 44px utility rail, independent Calendar and Tasks overlays, independent pin state, 260ms hover close delay, and no dashboard reflow.
- `frontend/src/components/Pages.tsx` is project-specific. It implements H/M/S timer editing, application management, report periods, and safe options persistence against the typed bridge.
- `frontend/src/components/DataTable.tsx` uses TanStack Table directly for filtering and sorting.

## Verification boundary

The React interface is verified against the approved TARGET images in `references/`; `CURRENT_implementation_do_not_copy.png` is used only as a negative baseline. Preview testing uses `preview-data/` and a `.Preview.exe` host whose bridge rejects every live command, including opening the native application. Visual QA therefore cannot start, pause, reset, or reconfigure the installed watcher.

No claim is made that locked 21st.dev `Component.tsx` source was retrieved, reconstructed, or copied. Those earlier references remain design research only and are not the runtime source of the React interface.
