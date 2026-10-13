# Game Management v5 design system

All future interface additions follow `DESIGN_SPEC.md`, this implementation summary and `ModernDesign.cs`. The specification and TARGET references take priority.

The application stays on WinForms and .NET Framework 4. No new runtime dependency is required.

| Token | Value |
|---|---|
| Primary | #2563EB |
| Work accent | #2563EB |
| Game accent | #EF4444 |
| Background / surface | #F8FAFC / #FFFFFF |
| Primary / secondary text | #0F172A / #64748B |
| Borders | #E2E8F0 |
| Success / warning / error | #16A34A / #D97706 / #DC2626 |
| Font | Segoe UI |
| Spacing scale | 4, 8, 12, 16, 20, 24, 32 px |
| Standard / primary / timer action height | 36 / 40 / 42 px |
| Compact action / input height | 32 / 36 px |
| Standard icon / metric holder | 18 / 44 px |
| Card / timer / button radius | 14 / 16 / 12 px |
| Card / large panel padding | 16 / 24 px |

Use ModernTheme, ModernCard, ModernButton, ModernProgress, IconStatCard, StatusBadge, ModernActionColumn and ModernAlertForm. Native numeric, date, checkbox, table, and file-picker controls remain native Windows controls, with the same font and surrounding spacing. Segoe MDL2 glyphs and drawn monochrome icons are used for navigation; no emoji icons are used. Responsive layout callbacks use ModernTheme.Px/ScaleFactor to preserve logical dimensions at larger display scales. Native field heights retain Windows behavior.

Navigation is the embedded top dock: Home, Applications, Timer Settings, Reports, Options. Keep Windows caption controls separate from the dock and preserve Tab / Shift+Tab access, Left/Right/Home/End movement and Ctrl+1 through Ctrl+5 navigation. The application has resize hit testing, maximize/restore, a minimum 800 x 600 window, and scrolling content.

Home has one reusable dashboard builder for Work and Game. Both show three top cards for session time, completed cycles and available CPU/GPU temperatures, countdown/phase/progress, resume/pause/reset, selected applications, a timer-settings summary and recorded session counts/totals/history. Work uses blue; Game uses red. Smaller windows stack the lower cards and scroll.

Calendar and Tasks float over Home from a 44 px icon rail in a 280 px overlay column. Calendar is above Tasks when both are open. They do not reduce dashboard width. Preserve hover, click, focus, Escape, independent remembered pins, and 300 ms delayed close behavior. Menus keep Tasks open during interaction. A mode change must restore their z-order above the active dashboard. Date selection filters the Home overview and Reports.

The shared dialog base styles timer alarms, session summaries, timer reset, discovery, and retained dialogs. ModernAlertForm supplies an icon, heading, description and labeled actions for ordinary confirmations. Bind Enter/Escape explicitly. Time-up alarms remain topmost and continue to work when the main window is minimized.

Hover and navigation transitions last 170 ms; overlay reveals last about 200 ms. Respect the Windows animation preference through ModernTheme.ReducedMotion. Do not animate dashboard geometry when an overlay opens.

State and compatibility mapping:

| Interface | Existing state / behavior |
|---|---|
| Work / Game dashboards | MainForm labels and existing timer/status/sensor refresh methods |
| Mode buttons | RequestManagementMode, asynchronous settings handoff and watcher acknowledgment |
| Timer editors | Existing GameSettings seconds/minutes fields and DurationInput |
| Application grids | Existing foldersList / workAppsList; executable paths remain intact |
| Save actions | Existing SaveSettingsAsync atomic replacement, rollback and watcher restart |
| Reports | WeeklyReport.Load and recorded active intervals; paused gaps are excluded |
| Options | Existing startup, notification, power/brightness, backup/restore and diagnostic actions |
| Tasks | New atomic tasks.json storage with validated records and malformed-file preservation |

Files:

- GameManagement.cs: preserved backend integration, settings, sensors, detection, alarms and compatible data models.
- ModernDesign.cs: reusable tokens and components.
- ModernDesktop.cs: application chrome, navigation, Applications, Timer Settings and Options.
- ModernHome.cs: shared Work/Game dashboard layout and existing state bindings.
- ModernAlerts.cs: consistent confirmation and information dialogs.
- ModernSessionOverview.cs: date-filtered recorded session metrics and recent history.
- ModernPanels.cs: Calendar, Tasks, task storage and pin preferences.
- ModernReports.cs: real date-range reports, charts, session search and CSV export.
- GameManagement.ps1: unchanged production watcher and timer/session engine.

Long-break scheduling and maximum-cycle limits are omitted because the existing timer engine does not support them. Do not add decorative controls for those functions. Implement their behavior and focused tests before adding controls.
