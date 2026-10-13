# GameManagementApplication — Agent Instructions (Sol 5.6 Ultra)

**These are project-level, binding design and quality constraints.** Read `DESIGN_SPEC.md` and the `references/` directory before modifying UI code. The target is the existing Windows application; **do not create a new unrelated app**.

## Source of truth

1. Behavior and constraints in `DESIGN_SPEC.md` (highest priority).
2. `references/TARGET_dashboard_full_window.png` and `references/TARGET_dashboard_main.png` (approved target look).
3. `references/TARGET_states_and_design_board.png` (examples of collapsed/hover/game states; **presentation material**, not the literal app frame).
4. Source components from 21st.dev, inspected via **Copy code** rather than Copy prompt, only where compatible.
5. `references/CURRENT_implementation_do_not_copy.png` is a **negative baseline**, not a style target.

Never reproduce explanatory right-side panels, design swatch boards, mini screenshots, or prompt labels inside the actual application.

## Required operating sequence

### Phase 0 — Inspect, then plan

- Detect UI framework, runtime, package manager, app entrypoint, tests, resources, and version control status.
- Identify UI modules, timer services, mode switching, application discovery, persistence, reports, hardware monitoring.
- State which 21st.dev components can be copied directly, must be ported, or cannot be accessed; list dependencies and licensing concerns.
- Produce a prioritized **reference vs current gap checklist** with code-file owners. Do not guess the framework.

### Phase 1 — Create central theme and layout primitives

- Add theme tokens and common components appropriate to this codebase.
- Never duplicate ad-hoc colors, random padding, component sizes, or radii across screens.
- Build/retain modern Windows title bar; center dock-like **Home, Applications, Timer Settings, Reports, Options** *inside* top bar.
- No macOS traffic-light decorations; no separate bottom/outside dock; no permanent navigation sidebar.

### Phase 2 — Fix Home geometry and panel interactions

- Match approved dashboard content in order: mode row → equal metrics → large timer → applications → settings/statistics 2-column grid.
- Implement slim left icon rail and two independently auto-hideable/pinnable **overlay** panels (Calendar, Tasks).
- The panels MUST overlay without reflowing the dashboard, cancel close timers during interaction, support pin and keyboard, preserve preferences.
- No Show Calendar / Show Tasks title buttons.

### Phase 3 — Complete app screens and wire state

- Work and Game must have consistent structure, separate colors and independent real app data.
- Apply target to Applications, Timer Settings, Reports, Options and alerts.
- Task management is functional and persisted; calendar date presets actually select ranges.
- Preserve existing timer calculations, pause/resume/reset state, mode-switch behavior, app discovery and paths, session tracking/history, reports and temperature monitoring.
- Missing sensor: display unavailable; missing apps: designed empty state; no synthetic data in production.

### Phase 4 — Measure, screenshot, compare, correct

- Launch the real app when environment permits, capture real screenshots at useful viewport sizes, compare against target references.
- Measure misalignment/size/spacing differences. Fix the **largest structural divergence first**, then spacing, typography, colors, microstates.
- Verify Work, Game, panel hover/hide/pin, all pages, timers, dialogs and minimum-window behavior.
- Run actual available tests and report test names/results; never claim tests you did not run.

## Pixel-spec summary (see full specification)

- Palette: background `#F8FAFC`, cards `#FFF`, borders `#E2E8F0`, text `#0F172A`, secondary `#64748B`, work/nav `#2563EB`, game `#EF4444`.
- Font: Segoe UI; body 13–14, section 14–16, main timer 48–56 logical px.
- Spacing: **4, 8, 12, 16, 20, 24, 32**; card radius 14px; timer radius 16px.
- Title bar ~48px; top dock ~44px, tabs ~36px, icon ~16px.
- Rail 44px, triggers 36px; overlay panels 280px with 16px padding; auto-dismiss after ~200–350ms if unpinned.
- Mode buttons 184×48; status pill 34px tall; metric cards ~84px tall with 44px icon circles.
- Timer card min height ~220px, 24px padding; progress 8px, buttons 132×42.
- Apps table header 36px, rows 56px, action buttons 32×32.
- Bottom cards ~16px gap, width split ~1:1.15; mini stat cards ~62px tall, history rows ~32px.
- Values are logical px at 100% scaling; adjust for DPI, readable content, and smaller screens instead of clipping.

## Non-negotiable constraints

- No emojis, no random redesign, no copied explanatory poster decorations.
- A React/TypeScript desktop frontend is explicitly approved by the user's newer integration brief. Use actual free shadcn/ui and Magic UI source where compatible, keep the .NET Framework/PowerShell backend authoritative, and retain the WinForms interface as a fallback during migration.
- Do not break functional logic or saved user data. Do not silently delete/replace configuration.
- Never claim direct original code integration if you only used a screenshot; source must actually be retrieved.
- Do not finish while Calendar/Tasks are missing, if they can be implemented in the available environment.
- Do not stop at build success: deliver **visual verification or an accurate statement of why it was impossible**.

## Existing application preservation requirements

- Preserve the existing native .NET Framework 4 WinForms architecture as the backend/fallback and keep its `ModernDesign.cs` tokens and responsive behavior valid. The React frontend must use matching centralized CSS tokens and a typed, local-only bridge rather than duplicating timer calculations in browser code.
- Preserve exact-second timer calculations, pause/session checkpoints, independent Game/Work cycles, foreground assignment rules, settings/data compatibility, and watcher handoff/rollback. Avoid backend rewrites for UI work.
- Preserve the user's selected timer scope: long-break scheduling and maximum-cycle limits are not implemented by the retained engine and must not be exposed as functional controls.
- Run relevant feature/lifecycle tests plus `Test-GameManagementV5.ps1` and `Test-GameManagementIntegration.ps1`. Compile with `Build-GameManagement.ps1` from Windows PowerShell `-STA`.
- UI verification must use `MainForm.PreviewMode` with an isolated `PreviewRoot`; never enable or pause the installed live watcher during a preview test. Production actions remain functional.
- Keep the five-destination top navigation inside the Windows application and Calendar above Tasks in one overlay column with remembered independent pins.

## Deliverables for each meaningful pass

1. Gap checklist (before and after)
2. Files changed and why
3. Components integrated vs faithfully ported
4. Actual build/tests and results
5. Screenshot(s) of real running app when possible
6. Remaining mismatches/blockers

## Completion rule

**Implement the approved UI exactly in structure and closely in visual appearance, with real interactions, using measured comparison and test evidence.** If details conflict, follow `DESIGN_SPEC.md` while preserving the explicit timer-engine scope above.
