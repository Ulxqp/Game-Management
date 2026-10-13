# GameManagementApplication — High-Fidelity Design Specification

**Status:** Approved target UI specification for implementation by Sol 5.6 Ultra  
**Platform:** Existing Windows desktop application, *framework to be verified from source*  
**Visual theme:** Windows-inspired light UI, precise alignment, subtle depth, no emoji  
**Primary reference:** `references/TARGET_dashboard_full_window.png`  
**Secondary close-up:** `references/TARGET_dashboard_main.png`  
**States and patterns:** `references/TARGET_states_and_design_board.png`  
**Known incorrect implementation:** `references/CURRENT_implementation_do_not_copy.png`

> **Priority order if references conflict:** (1) explicit user behavior requirements in this specification, (2) the **target main window**, (3) the approved component reference styles, (4) illustrative secondary wireframe states. **Never copy** the current implementation as the visual target. Do not reproduce the presentation board's instructions, color legend, thumbnails, outer background, or commentary as UI.

## A. Intended UI and scope

This is a working app, not a marketing mockup. Deliver these navigable screens: **Home** with both **Work** and **Game** variants, **Applications**, **Timer Settings**, **Reports**, **Options**, and modal/time-up alerts. Work and Game share a single layout and reusable controls. The Home screen includes a **Calendar panel**, a **Tasks panel**, a Work/Game switch, status badges, three metric cards, a large timer card, a selected-application table, a settings card, and session-statistics card. Panels are individually revealable and pinnable from a narrow utility rail. No permanent full-height navigation sidebar. Top navigation is **inside the Windows window**, visually dock-like but not a macOS window.

The approved screenshots are conceptual renderings, not reliable proof of existing application data or real software features. Use **actual current application state**, never printed demonstration values.

### A1. Coordinate and measurement rules

All dimensions below are **design-space / logical pixels at 100% scaling**. Translate to the project's UI framework's density-independent or DPI-aware units; do not force physical-screen pixels on a scaled Windows monitor. Target reference image sizes differ because some are presentation collages. Measure **inside the application window**, not the whole collage. Fixed values establish component rhythm; responsive layout and text clipping take priority over literal fixed coordinates when window size changes.

- Desktop layout design target: about **1470 × 768 logical px**, with a taller-window check at **1536 × 1024**. Also test **1280 × 880** and Windows DPI scaling at 125%/150% if feasible.
- Inner horizontal gutter: **16–24px**; default **20px** when there is room.
- Stacked section spacing: **12–16px**, default **16px**.
- Main dashboard should scroll *within the content area* on short windows; never crop or overlap core timer controls with title bar.
- The current implementation's large blank zones and unstructured paragraphs are not faithful and must be replaced.

## B. Design tokens (one source of truth)

| Token | Value | Usage |
|---|---|---|
| `color.bg` | `#F8FAFC` | Window content background |
| `color.surface` | `#FFFFFF` | Cards, panels, inputs |
| `color.surfaceMuted` | `#F1F5F9` | Inactive controls, table headers |
| `color.border` | `#E2E8F0` | 1px card and control borders |
| `color.divider` | `#EDF2F7` | Subtle separators |
| `color.text` | `#0F172A` | Main text and values |
| `color.textMuted` | `#64748B` | Supporting labels |
| `color.textFaint` | `#94A3B8` | Hint or unavailable text |
| `color.blue` | `#2563EB` | Navigation active / Work mode |
| `color.blueHover` | `#1D4ED8` | Work primary hover |
| `color.blueSoft` | `#DBEAFE` | Selection / badges / icon circles |
| `color.red` | `#EF4444` | Game mode/action |
| `color.redHover` | `#DC2626` | Game action hover |
| `color.redSoft` | `#FEE2E2` | Game selection / badge |
| `color.green` | `#16A34A` | Safe / successful status |
| `color.greenSoft` | `#DCFCE7` | Active status background |
| `color.amber` | `#D97706` | Warning |
| `color.amberSoft` | `#FEF3C7` | Warning background |
| `color.focus` | `#93C5FD` | Visible keyboard focus |

- Font: **Segoe UI**; fallback to a compatible Windows sans-serif where necessary.
- Font weights: 400 body, 500 small labels, 600 titles/buttons, 700 critical numerical values.
- Font sizes: title bar **14px**; page heading **20–24px**; section heading **14–16px**; body **13–14px**; labels **12px**; tiny hints **11–12px**; main timer **48–56px**.
- Line-height: generally **1.35–1.5**; large timer about **1.05–1.12**.
- Spacing scale (only standard choices): **4, 8, 12, 16, 20, 24, 32px**.
- Radii: small **10px**, buttons **10–12px**, cards **14px**, large floating panels/timer **16px**, pill **999px**.
- Border: `1px solid #E2E8F0`.
- Card elevation: subtle, e.g. `0 1px 2px rgba(15,23,42,.04), 0 8px 24px rgba(15,23,42,.06)`; **no heavy drop shadows**.
- Control sizes: compact **32px**, default **36px**, primary **40px**, major timer button **42px**.
- Icon sizes: **16px** nav/compact, **18–20px** general.
- Hover durations: **150–200ms**; overlay reveal **180–250ms**; disable or reduce animations when user requests reduced motion.

Do not hardcode the same appearance separately on multiple screens. Implement theme resources/variables, sizing constants, and reusable widgets appropriate to the existing framework.

## C. Top title bar and dock navigation

| Element | Target |
|---|---|
| Window title bar | ~**48px** high, **16px** horizontal inner padding |
| App glyph | **18 × 18px**; app name **14px** bold |
| Window controls | Windows Minimize, Maximize/Restore, Close; approximately **46 × 32px** each with appropriate hit targets |
| Navigation | **44px** tall integrated top dock container, centered within free title-area space |
| Tab | **36px** height, **≥88px** width, **12px** radius, **8px** between tabs |
| Nav icon/label | **16px** icon above **12px** label or approved compact icon/label layout |
| Selected tab | soft-blue fill `#EFF6FF`, blue icon/text; optional 2px bottom active indicator |

Exact destinations: **Home**, **Applications**, **Timer Settings**, **Reports**, **Options**. The dock is navigation, not a launcher for operating-system applications. It MUST be **inside** the window, not below it or floating over content. Provide keyboard navigation and accessible labels. Avoid an oversized top row and avoid overlapping native Windows drag/min/max/close zones. No macOS traffic-light buttons, no permanent left navigation list, no extra “Show Calendar” or “Show Tasks” title buttons.

## D. Left utility rail and panels

### D1. Rail
- Leftmost icon rail width **44px**; triggers **36 × 36px**, radius **10px**, at least **8px** separation.
- One trigger for **Calendar**, one for **Tasks**. Active/hover treatment `#EFF6FF` / `#DBEAFE` with blue icon.
- Rail should remain independently accessible while panels are hidden.

### D2. Overlay / pin / auto-hide behavior
- Panel width target **280px**, card radius **14px**, internal padding **16px**.
- **Calendar** and **Tasks** can each be hidden, hovered open, or pinned open **independently**.
- Hovering trigger opens the associated panel. Moving from trigger into panel must **not** close it.
- Unpinned panel closes after ~**200–350ms** once cursor and keyboard focus leave both trigger and panel. Cancel that closing when mouse re-enters.
- Click or keyboard activation toggles a panel. Pin keeps it open until explicitly unpinned; persist pin preferences.
- **Crucial behavior requirement:** Panels visually **overlay** the main page while open. They must NOT reflow or resize the underlying dashboard. The target screenshots depict how expanded panels should look, but interaction behavior follows this no-reflow rule. Place overlays above cards, below dialogs. Maintain logical stacking and focus management.
- If both pinned panels appear stacked, ensure the bottom panel and any controls remain reachable by panel scrolling or window scrolling; panel height must fit the viewport.
- No abrupt layout jumps when mouse leaves the side rail.

### D3. Calendar detail
- Header: title **14px/600**, icon **16px**, collapse/pin controls.
- Current month label **14px/600**; previous/next buttons.
- Presets: Today, This Week, This Month, Custom — **28px** height, horizontal padding **10px**, radius **10px**, **12px/500** text.
- Weekday/calendar cells **32 × 32px** or equal-width 7-column grid, **12px** dates. Today outline blue; selected date blue fill with white text; date-range interior light blue.
- Selected-range summary **≥48px**, internal padding **12px**, radius **12px**.
- Actual system date and functional presets; support keyboard, date boundaries, and localization of week start if app already supports it.
- Reference: `https://21st.dev/@originui/components/calendar/range-calendar-with-presets`.

### D4. Tasks detail
- Header **14px/600** with close/pin controls.
- Filters All, Work, Game, Personal; chips **28px** high, **10px** x-padding, **10px** radius, **12px** text.
- New-task input **36px** high and add control **36 × 36px**.
- Task row **32–36px** minimum, checkbox **16 × 16px**, category dot **8px**, text **13px**, category chip **22px** high.
- Work badge light blue, Game badge light red, Personal neutral; completed tasks visually distinct without disappearing unexpectedly.
- Completion progress footer: text **12px**, bar **8px** high, rounded.
- Add/edit/complete/delete/filter with persistent storage and no interference with timer business logic.
- Reference: `https://21st.dev/@vaib215/components/todo-task-management`.

## E. Home dashboard geometry

### E1. Main stacked structure

1. **Mode row**, at top of content, ~**48px** tall. Work/Game toggle left, status pill right.
2. **Three metrics**, equal-width responsive columns separated by **16px**.
3. **Large timer**, full available width.
4. **Selected Applications**, full width.
5. **Bottom pair**: Timer Settings on left, Session Statistics on right, split about **1 : 1.15** with **16px** gap.

Content should align to the **same left and right edges** across the timer, applications table, metrics, and bottom card pair. Avoid oversized whitespace. In actual app the left overlay obscures content temporarily; it must not permanently subtract 280px from content width when unpinned.

### E2. Mode row
- Buttons Work Mode, Game Mode: **184 × 48px** each at roomy width; **12px** radius; **14px/600**, icon **16px**.
- Active Work fill `#2563EB`, Active Game fill `#EF4444`, white text; inactive surface white/subtle border.
- Status pill: **34px** height, horizontal padding **14px**, pill radius, dot **8px**, text **13px/600**. Text reflects actual timer state (`Work active`, `Work paused`, `Game ready`, etc.).

### E3. Metric cards
- Three cards: Session time, Completed cycles, GPU temperature / peak (CPU availability secondary if supported).
- Each target **84px** high, **16px** padding, **14px** radius; same heights and top/bottom alignment.
- Circular icon holder **44 × 44px**; label **12px/500** muted; metric value **20px/700**.
- Show actual sensor readings; `Unavailable` for unknown values. Never fabricate a “Safe” classification from missing or invalid temperatures.

### E4. Main countdown card
- Card minimum height **220px** when viewport supports it; padding **24px**; radius **16px**.
- Caption `WORK TIMER · Focus session` or `GAME TIMER · Play session` (+ paused/break phase from actual state), centered **14px** muted.
- Big timer **56px/700**, centered; use monospaced or tabular numerals to eliminate width jitter.
- Progress track height **8px**, rounded; fill blue in Work, red in Game; accurately derived from timer state.
- Action row centered; buttons **132 × 42px**, **12px** gap, **12px** radius, **14px/600**. Start/Resume, Pause, Reset must maintain their existing enable/disable rules; do not make every action active simultaneously when illogical.
- Keep timer/progress/button group vertically compact and evenly spaced.

### E5. Applications section
- Outer card **14px** radius, **16px** padding, thin border. Header left icon + heading **14px/700**; right count **12px** and Add Application control **36px** high.
- Table header **36px** high, muted surface and **12px/600** labels.
- Body rows **56px** high, **13px** text; app icon **20px**; actions **32 × 32px**, **10px** corner radius, **14px** glyph.
- Columns: name/icon, executable path, optionally status, actions; do not sacrifice path legibility when window shrinks. Allow clipping with tooltip or horizontal table scrolling.
- Empty list: centrally aligned, **≥40px** vertical padding and an Add action; do not invent example apps.
- Distinguish app selected/detected/running through real data. Maintain existing launch/browse/remove operations.

### E6. Timer Settings card (bottom left)
- **14px** radius, **16px** padding, clear heading and Configure button (**34px** high).
- Structure actual settings as two aligned interior groups rather than a paragraph:
  - work/game & break durations: numeric hours/minutes/seconds fields **32px** high, approx. **56px** width as appropriate;
  - limits & longer-break controls;
  - enable timer, auto-switching, sound, notifications as appropriate.
- Labels **12px/600**, values **13px** body, checkbox rows **24px** min with **8px** gap.
- When showing a summary rather than editable inputs, use label/value rows with consistent alignment and offer Configure to open editable settings. Don't simulate interactive inputs without persistence.

### E7. Session Statistics card (bottom right)
- **14px** radius, **16px** padding, title **14px/700**, period dropdown **34px** tall and at least **96px** wide.
- Four mini summary tiles for Work Sessions, Total Work Time, Game Sessions, Total Game Time; tile target **62px** high, radius **12px**, padding **12px**, labels **11px**, values **16px/700**.
- History grid header **34px** high, rows **32px** high, text **12px**, status dots **8px**.
- Use actual history, not mock statistics. Keep session time columns aligned with tabular numerals.

## F. Secondary pages

### Applications
- Reuse top navigation, left utility rail when appropriate, theme tokens, cards, tables.
- Work/Game applications tabs; add via installed app chooser or executable browse; manage entries and statuses; search/filter when available. Preserve existing data and operating-system integrations.

### Timer Settings
- Dedicated organized editing page, grouped Work/Game duration, break and long break, cycles, auto mode, sound, notifications, enable timer, reset defaults, Cancel/Save.
- Validate min/max and persist according to existing app rules. No unimplemented toggles.

### Reports
- Period controls Daily/Weekly/Monthly/All Time + valid date filtering; summary cards, history table and real export if already supported; charts only when data is available.

### Options
- Group actual options into General, Appearance, Notifications, Detection, Data/Storage, Advanced, only when that configuration exists; avoid invented preferences.

### Game Mode
- Same component hierarchy and dimensions as Work. Swap to **red** active mode/progress/primary action. Use game app data and gaming timer. Do not switch to an entirely different dark or purple design unless separately requested.

## G. Dialogs and notifications

Reference: `https://21st.dev/@ekmas/components/alert/alert-demo` — use its source implementation **only if framework-compatible**, otherwise port the design faithfully. Show distinctly styled messages for Time Up / Work Session Ended, Game Session Ended, Break Ended, Remove Application, Switch Mode, No App Selected, Save Changes, and errors. Titles typically **15–16px/600**, description **13–14px**, controls **36–40px** tall; match theme radii and borders. Timer expiry alerts must use actual timer state, avoid repeat firing, and honor automatic switching. Use system-level Windows notification support only when already available/appropriate. No emojis.

## H. Component source references / actual code requirement

For each selected 21st.dev page inspect **Copy code**, including `Component.tsx` and `Usage.tsx` when available; do **not** use only **Copy prompt**. Record dependencies and license. Adapt to existing framework rather than blindly dropping React TSX into a native UI:

| Design function | 21st.dev source |
|---|---|
| Dock navigation | https://21st.dev/@n38693842/components/mac-os-dock |
| Calendar presets | https://21st.dev/@originui/components/calendar/range-calendar-with-presets |
| Todo task management | https://21st.dev/@vaib215/components/todo-task-management |
| Alert | https://21st.dev/@ekmas/components/alert/alert-demo |
| Overview metric cards | https://21st.dev/@uniquesonu/components/dashboard-overview |
| System monitor | https://21st.dev/@isaiahbjork/components/system-monitor |
| App and report tables | https://21st.dev/@ravikatiyar162/components/project-data-table |

If source code cannot be accessed, document that and do not claim to have copied it. Copy code only insofar as license and technical compatibility permit. The target reference images override component galleries' default styling.

## I. Responsive and accessibility behavior

- With overlays hidden, Home uses the entire usable content width minus the rail and gutters.
- At **1280 × 880**, maintain legibility, equal metric sizes, and functioning timer. At narrower widths, gracefully wrap stats and stack bottom cards. Use scrolling rather than squeezing text to unreadable sizes.
- At scaled DPI, measure in appropriate logical units. Never force physical pixel values causing clipped controls.
- Hover/click/pin overlay interactions plus keyboard support; focus visibly indicated; dialogs are focus-managed and accessible.
- Preserve app startup, window drag/maximize/minimize/restore, Windows taskbar and DPI behavior.

## J. Implementation & validation requirements

1. **Audit first:** framework, UI files, state/services, backend risks. Create a screen/component map and a gap table comparing actual app to target.
2. **Design tokens first:** eliminate competing scattered styles; introduce reusable components.
3. **Structural fix first:** title+nav, rail, overlays, dashboard grid; THEN styling refinements.
4. **Wire real behavior:** existing timers, app detection, history, persistence; implement new task/date services only where actually needed.
5. **Build and run:** launch the actual app where tools permit. Capture screenshot(s); compare against the cropped target window, not board decorations.
6. **Verify multiple states:** Work active, Work paused, Game active, no apps, apps populated, Calendar hover/pin/hidden, Tasks hover/pin/hidden, both hidden, time-up alert, all nav pages, resized window.
7. **Regression test:** session timing, mode switching, app detection, path persistence, settings, history, hardware fallback; log actual tests and failures.
8. **Repeat:** fix top visual mismatches in each iteration. Provide before/after screenshots or a frank limitation if screenshot capture unavailable.

### Visual discrepancy checklist

- [ ] Top navigation is centered in title bar with no collision.
- [ ] Left utility rail is narrow and panels slide over, not push, dashboard.
- [ ] Work/Game segmented controls match approved proportions.
- [ ] Stats are three equal-height, aligned cards.
- [ ] Countdown, progress track, buttons are centered and compact.
- [ ] Selected apps is a structured, readable table.
- [ ] Lower section has genuine 2-column settings/stats cards.
- [ ] No dead controls, placeholder pages, fabricated readings, or unexplained empty space.
- [ ] No content copied from poster-side legends or mini-preview captions.
- [ ] All modes and pages use identical design tokens.
- [ ] No emojis or macOS traffic-light titlebar styling.

## K. Completion definition

The task is complete **only** when the approved UI structure and interactions are present, business logic is preserved, and verification results are reported honestly. If blocked by missing browser/source access, Windows GUI launch capability, or framework restrictions, explain precisely what could and could not be done. Don't substitute a new design or falsely report completed QA.
