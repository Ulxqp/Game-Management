# Paste this into Sol 5.6 Ultra after attaching the project/repository and reference bundle

Act as a senior Windows desktop application engineer, design systems architect, and visual QA specialist. Work in my **existing GameManagementApplication** repository. This is a corrective implementation: the previous agent output does not faithfully match my approved high-fidelity wireframes.

Read `AGENTS.md` and `DESIGN_SPEC.md` **completely** before edits. Open all four `references/` images and explicitly distinguish **TARGET** from **CURRENT**. `TARGET_dashboard_full_window.png` and `TARGET_dashboard_main.png` are the visual target; `CURRENT_implementation_do_not_copy.png` is the baseline to fix. `TARGET_states_and_design_board.png` illustrates states, but its legends/miniatures are not part of the actual app.

**Priorities:**

1. **Investigate the codebase.** Identify its actual UI framework, main window, components, state/services, timer, auto detection, session reports, hardware monitoring, stored data, and test commands. Don't assume React or Tkinter. Explain feasibility briefly and then implement.
2. **Use exact design constraints.** Tokenize colors, spacing, sizing, typography, and corner radii specified in DESIGN_SPEC.md. Use Windows-style window controls and the centered icon+label navigation dock integrated into the top title area. Remove any unrelated/floating/bottom dock or full vertical sidebar.
3. **Rebuild the approved Home layout.** Work/Game selector, three equal cards, prominent compact centered timer, applications table, and bottom Timer Settings/Session Statistics cards. Match the proportions/padding/gaps; do not substitute plain text for the designed cards.
4. **Implement both left overlay panels.** Small edge rail triggers Calendar and Tasks. Hover/click expands; pointer leaving auto-hides if not pinned; the panels stay open during interaction. Pin preserves open state and is remembered; keyboard interaction works. **Never shift or shrink the dashboard when a panel appears**; use proper overlay and stacking behavior.
5. **Implement the real Calendar and Tasks.** Calendar uses range selection and presets; Tasks supports add/edit/complete/delete, Work/Game/Personal filters, and persistence.
6. **Complete every page.** Home (Work and Game), Applications, Timer Settings, Reports, Options, and timer-completion/confirmation alerts. Hook every new control to actual services; don't use mock data or fake temperature readings.
7. **Retrieve actual component source.** When visiting the provided 21st.dev URLs, choose **Copy code, NOT Copy prompt**, inspect source/dependencies/licensing. Reuse directly only if the framework supports it; otherwise faithfully port appearance and behavior. Do not claim to have copied code unless it was actually retrieved.
8. **Visually verify.** Launch the real application, capture screenshots, compare with provided target at desktop sizes, correct structural discrepancies first, repeat. Test timing, modes, app detection, persistence, resize, sidebar states, keyboard and alerts. If tooling prevents screenshot capture, state so explicitly, and do not falsely report a matched screenshot.

Do not change existing business logic unnecessarily. Do not migrate the UI framework without necessity. Never use emojis, macOS traffic-light window buttons, poster-style annotations, mock app lists, or presentation thumbnails in the real UI.

**Workflow rule:** Before editing, output a concise audit and a prioritized gap list. Then implement the fixes rather than merely planning. After each substantial stage, run the app/test if possible and show measured remaining mismatches. Do not declare success while the approved Calendar/Tasks behavior, unified layout, or secondary pages are missing.

**Final deliverables:** changed files and rationale; which design components were directly reused vs ported; screenshots of the actual running app; available test results; and an honest incomplete-items list. Success requires visual fidelity to the attached target, not just passing a build.

### Original design references (choose Copy code)

- Navigation: https://21st.dev/@n38693842/components/mac-os-dock
- Calendar: https://21st.dev/@originui/components/calendar/range-calendar-with-presets
- Task management: https://21st.dev/@vaib215/components/todo-task-management
- Alerts: https://21st.dev/@ekmas/components/alert/alert-demo
- Stats overview: https://21st.dev/@uniquesonu/components/dashboard-overview
- System monitor: https://21st.dev/@isaiahbjork/components/system-monitor
- Data tables: https://21st.dev/@ravikatiyar162/components/project-data-table
