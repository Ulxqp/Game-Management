GAME MANAGEMENT 5.0.2
=====================

A Windows desktop controller for selected Work applications and automatic Game
sessions, with independent focus/break timers and persistent activity history.
Version 5 adds the modern interface described in DESIGN-SYSTEM.md. Version 5.0.2
applies the corrective DESIGN_SPEC.md and target references, with measured
dashboard, panel, table, typography, and interaction refinements.

RUN OR UPGRADE
--------------
Use GameManagement-Setup.exe to upgrade the existing installation, preserving
settings, session history, timer checkpoints, and completed cycles. The ZIP
contains the application, source, tests, and default settings. Data stays in
Documents\GameManagement for compatibility with earlier versions.

HOME
----
Choose Work Mode or Game Mode. Each mode retains its own phase, time remaining,
and cycle total. Start / Resume enables monitoring; Pause freezes countdown and
active session time and restores managed system settings. Reset opens separate
Reset Timer, Reset Cycles and confirmed Reset All actions.

The dashboard shows actual session time, selected applications, CPU/GPU
current/peak temperatures and safety classification, and saved activity for the
selected date range. Compact session cards show real Work/Game counts and totals.
Unavailable sensors are labeled honestly. A cycle completes after both the
session and its break finish. Sessions of two minutes or less remain excluded
from saved history, and paused time is excluded from reported totals.

CALENDAR AND TASKS
------------------
Use the icons at the left edge of Home. Hover or click to open; unpinned panels
collapse shortly after pointer and keyboard focus leave. Pin either panel to
keep it open. Pins are remembered across restarts. Calendar sits above Tasks
when both are open. Both panels overlay the dashboard without resizing content.
Calendar supports monthly navigation, Today/This Week/This Month/Custom dates
and date ranges. Selected dates filter Home statistics and Reports. Tasks support
Work/Game/Personal categories, add/edit/complete,
remove/undo, counts and progress. Tasks save to tasks.json on this computer.

APPLICATIONS
------------
Select the Work Applications or Game Applications tab. Browse executable adds
portable apps; Discover installed opens the existing background discovery
picker. Search by name/path. Game library folders remain supported. Launch uses
the executable's working folder; Location opens its folder. Remove requires
confirmation. Save selections applies changes without duplicating paths.

TIMER SETTINGS
--------------
Work, Game and both break timers support hours/minutes/seconds from one second
through 24 hours. Existing minute settings load unchanged. Save timers applies
changes; Discard edits reloads stored values. Restore defaults loads pending
defaults for review before saving. Sound and time-up alert switches stay synced
with Options. Automatic mode switching follows
the assigned app in the foreground. Pending edits defer mode switching until
you save or discard them. A manual choice overrides detection until a different
assigned foreground app gains focus. Unassigned/conflicting apps are ignored.

REPORTS
-------
Choose Daily/Weekly/Monthly/All time or a custom date range. Charts, Work/Game
active-time totals, cycles and searchable session history use recorded data.
Export CSV writes the visible report. Active intervals exclude pauses and are
clipped to the chosen dates.

OPTIONS
-------
General: start at sign-in and keep the window in the notification area.
Notifications: time-up popup, alarm sound and end-of-session summary.
Detection/performance: scan interval, game brightness and foreground mode rules.
Data/storage: compatible settings/history backup and restore.
Advanced/appearance: activity log, diagnostics and retained pixel gerbera view.
All screens and dialogs use the same Windows design system.

WINDOW AND KEYBOARD
-------------------
Use the Windows minimize, maximize/restore and close controls. Drag the title
bar or resize window edges. Smaller windows scroll; controls stay accessible.
Tab / Shift+Tab move focus. Ctrl+1 through Ctrl+5 select navigation destinations.
Left/Right, Home and End also select pages while the top dock has focus.
The calendar/task rail supports keyboard focus, arrow movement and Escape.
Unpinned panels remain visible while they hold keyboard focus.

COMPATIBILITY AND VALIDATION
----------------------------
Windows with .NET Framework 4. No new runtime framework or dependency.
The existing AC-only power/brightness controls and safe restoration are kept.
Game process priority, GPU driver settings, clocks, voltage and firmware remain
untouched. CPU temperature availability depends on exposed sensors.

Time-up alarms remain topmost and support stopping sound even when the main
window is minimized. No Windows toast service was added. Long-break scheduling
and maximum-cycle settings are not exposed by this retained timer engine.
Tasks are stored separately from the compatible .gmbak settings/history backup.

See CHANGELOG.md, COMPONENT-REFERENCES.md, V5-IMPLEMENTATION.md,
INTEGRATION-TEST-REPORT.md, V5-TEST-REPORT.md and the feature/lifecycle
reports for changes, tested behavior and remaining validation limits.

Development: Build-GameManagement.ps1 compiles the application and runs all
checks, then builds/verifies the installer. Run through Windows PowerShell -STA.
The release ZIP keeps build scripts and C# files in Source, tests in Tests, and
documentation in Documentation (test reports in Documentation\Reports).
Run Source\Build-GameManagement.ps1 to rebuild an extracted release.
--preview and --render use an isolated preview-data folder and cannot enable or
pause production monitoring. Preview data is excluded from release packages.
