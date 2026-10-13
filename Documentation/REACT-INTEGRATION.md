# React desktop integration

## Status

This repository now contains a buildable React 19 + TypeScript view layer in `frontend/` and a separate .NET Framework 4 desktop bridge in `bridge/`. The existing `GameManagement.exe`, watcher scripts, settings schema, timer deadline/checkpoint files, cycle counters, session history and task schema remain authoritative and unchanged.

The host embeds the production Vite bundle in a WinForms `WebView2` control and serves it only on a random `127.0.0.1` port. The required WebView2 SDK assemblies and loader are vendored with their license and notice files. The single installed Start Menu shortcut opens this desktop host, so Windows exposes one consistent Game Management interface. The native WinForms executable remains in the install folder for recovery and bridge-only operations, but is no longer registered as a second application.

## Architecture

```
React UI (Vite static bundle)
        │ same-origin JSON RPC on random loopback port
        ▼
GameManagement.Desktop.exe (.NET Framework 4 + WebView2)
        │ reads compatible files / invokes fixed existing scripts
        ▼
GameManagement.ps1 + Pause-GameManagement.ps1
        │
        ├─ settings.json
        ├─ runtime-state.json
        ├─ timer-state.json / work-timer-state.json
        ├─ timer-counters.json / performance-state.json
        ├─ tasks.json (Version 1 + Items + CreatedUtc)
        └─ GameSessionHistory.txt
```

The bridge uses a small loopback-only TCP HTTP server because `HttpListener` is unavailable on the supported local runtime. Header and body sizes are bounded, static paths are contained under `frontend/dist`, and only four enumerated commands (`pause`, `resume`, `reset`, `openNative`) plus fixed settings, task, and panel-preference payloads are accepted. It has no RPC for arbitrary paths or command strings. POST requests require the host's loopback `Host` and same `Origin`. Settings and independent Calendar/Tasks pin preferences use atomic replacement. Timer resets use the existing validated request/acknowledgement file protocol. An explicit `--preview-root` stays under the project and rejects every live command, including `openNative`, so visual QA cannot signal, stop, or open the installed watcher. A `.Preview.exe` test copy uses the same isolated root for automation without adding live-data command-line arguments.

## Implemented interface

- Centralized Tailwind/CSS tokens matching the approved palette, spacing, radii and Segoe UI typography.
- Compact integrated five-destination dock using the actual Magic UI Dock source as its motion primitive.
- Work/Game dashboard views with real saved timer deadlines, paused remaining seconds, cycles, selected apps, session state and temperature snapshots.
- A fixed 44px utility rail with Calendar and Tasks overlays that never reflow the dashboard. Each overlay opens, pins, unpins, closes, and persists independently; hover close uses a 260ms delay. Tasks interoperate with the native `tasks.json` Version 1 document and support add, edit, complete, delete, filtering, and progress.
- Applications table with TanStack Table search and sortable headers, add-path validation, and confirmed removal. Native discovery/browse stays available through the fallback window.
- Timer settings with separate H/M/S inputs for all four durations, 1-second through 24-hour bounds, dirty-state detection, discard confirmation, and compatible atomic persistence while paused.
- Reports built with Recharts from real `GameSessionHistory.txt`, with Daily, Weekly, Monthly, and All Time periods and compatibility for older `Game:` / `Total game time:` records.
- Radix/shadcn-style destructive confirmations for timer reset, application removal, and discarding timer edits.

The Work/Game controls are labeled **Work View** and **Game View** because they select the visible dashboard. The retained native app remains responsible for the full live mode-switch handoff and automatic foreground arbitration. The React status badge distinguishes the authoritative active backend mode from the inactive ready view.

## Component provenance

Sources were retrieved on 2026-10-11:

- Magic UI Dock: `https://magicui.design/r/dock.json`, MIT. The registry source is retained in `frontend/src/components/magic-dock.tsx`; the local change is the `cn` import plus compact integration styling.
- shadcn/ui new-york-v4 Card: `https://ui.shadcn.com/r/styles/new-york-v4/card.json`, MIT. Its source pattern is adapted in `components/ui.tsx` to the approved 14px cards and compact padding.
- shadcn/ui new-york-v4 Alert Dialog: `https://ui.shadcn.com/r/styles/new-york-v4/alert-dialog.json`, MIT. The local source pattern uses the official Radix primitive package and the project's Button styling.
- shadcn/ui new-york-v4 Calendar: `https://ui.shadcn.com/r/styles/new-york-v4/calendar.json`, MIT. The calendar uses its declared `react-day-picker`, `date-fns` and Lucide stack with design-specific compact classes.
- TanStack Table: `@tanstack/react-table`, MIT.
- Recharts: `recharts`, MIT.
- bundui/shadcn-ui-kit `todo-app1`: the free registry entry was identified at `https://shadcnuikit.com/r/todo-app1.json`, but the source URL was not accessible to the retrieval tool. No claim is made that its implementation was copied. The task panel was implemented from the documented behavior with local shadcn primitives.

Dependency versions and transitive licenses are locked in `frontend/pnpm-lock.yaml`.

## Build and run

```powershell
cd frontend
pnpm install
pnpm typecheck
pnpm build
cd ..
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\bridge\Build-ReactDesktopHost.ps1
.\bridge\Start-ReactDesktop.ps1
```

For isolated preview data:

```powershell
.\bridge\Start-ReactDesktop.ps1 -PreviewRoot preview-data
```

The complete build compiles the native fallback, type-checks and bundles the React interface, builds the WebView2 host, runs the regression suites, and verifies the local installer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\Build-GameManagement.ps1
```

## Current limitations

- Full live backend mode switching, installed-app discovery, file pickers, backup/restore, startup registration and system power controls open the native fallback window.
- The Home date range filters the dashboard session summary. Reports use their own explicit Daily, Weekly, Monthly, or All Time period.
- The production bundle is about 970 KB before gzip (about 295 KB gzip). It can be split by route in a later performance pass.
- The host requires the Microsoft Edge WebView2 Runtime available on supported Windows installations. If initialization fails, `GameManagement.exe` remains available directly in the install folder for recovery.
- Visual smoke verification uses `--preview-root` so it cannot enable, pause, or reset the installed watcher.
