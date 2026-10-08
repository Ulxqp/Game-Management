GAME MANAGEMENT
=================

Game Management is a Windows 98-style desktop and notification-area controller
for automatic game performance sessions and user-selected work focus sessions.
Its data remains in Documents\GameManagement for upgrade compatibility.
Version 3.2.1 adds optional foreground-app mode switching, independent saved
Game and Work timers, per-mode reset controls, and persistent completed cycles.
Slow Windows, sensor, app-discovery, and watcher operations remain off the
interface thread.

RUN
---
Double-click GameManagement.exe.

INSTALLER
---------
Run GameManagement-Setup.exe to install or upgrade the application. Setup
preserves existing settings and startup/paused choices, creates Desktop and
Start Menu shortcuts, and adds Game Management to Windows Installed Apps.
Uninstalling safely restores captured Windows settings and keeps the settings
and activity log in Documents\GameManagement.

Build-GameManagementInstaller.ps1 rebuilds the single-file installer after
the application or its packaged files are updated.

HARDWARE COMPATIBILITY
----------------------
The universal installer supports Windows desktop and laptop PCs with NVIDIA
GeForce RTX 20- and 30-series graphics, including Ti, SUPER, and Laptop GPU
variants. Detection is informational: Game Management does not change GPU
drivers, clocks, voltages, firmware, or NVIDIA settings. Setup adapts to the
power controls exposed by each PC and also remains usable with other GPUs.
A HARDWARE-COMPATIBILITY.txt report is written during each installation.

CONTROLS
--------
Enable         Installs/starts Game Management and enables sign-in startup.
Pause          Stops the watcher, restores captured system settings, and
               disables sign-in startup without deleting the power plan.
Save settings  Saves brightness, scan interval, selected game apps, and folders.
Installed apps Adds a detected installed application by its exact executable.
Browse file    Adds a portable or otherwise unlisted game executable.
Add folder     Adds a launcher or standalone game-library folder.
Start at login Launches one hidden Game Management instance when you sign in.
Reports        Opens weekly/monthly totals and searchable session history.
Options        Changes timer/summary notifications or backs up/restores data.
Auto mode      Switches only when the foreground app is assigned to Game/Work.
Reset timer    Opens Reset Timer, Reset Cycles, and confirmed Reset All choices.
Game Management does not change game process priority.

GAME TIMER
----------
When enabled, the selected game timer starts as soon as the first game is
detected. When it finishes, the configured break timer starts automatically.
At the end of the break, a fresh game timer starts, creating a repeating
game/break cycle while the game remains open. The dashboard shows the active
phase, an exact MM:SS value beside Minutes left, and completed cycle count.
The visible timer display is updated independently for smooth display. Confirm applies
new game and break durations immediately. If the interface is closed, the
background watcher still tracks the timers and opens a topmost alarm. The
Windows alert sound repeats until Stop alarm is pressed. The alert uses short
messages such as "Time for a break!" and "Break over!" Closing the final game
cancels and resets the cycle.

Pause freezes the current phase and exact time left. Completed Game and Work
cycle totals are stored separately and increase only after a full break ends.
Enable keeps the value frozen until the matching game or work app is detected,
then continues the countdown from the same point.

Use Options to independently turn the timer popup, timer sound, and end-of-
session summary on or off. These choices do not stop timers or history saving.

The timer alarm and game-session summary use the same Windows 98-style navy
title bar, gray background, raised border, inset panels, and beveled buttons as
the main interface. The classic visual style remains unchanged.

BREAK CONTROL
-------------
The Press Escape option is enabled by default. At break start, Game Management
finds the detected game's real window, brings that window forward,
checks that it truly has focus, and only then presses Escape. The main
interface stays where it was; only the timer message appears. At break end,
the same safe check presses Escape again and shows the next timer message.
If Windows cannot focus the game, no key is sent and the reason is logged.

LIVE TEMPERATURES AND SESSION HISTORY
-------------------------------------
The Current status panel updates CPU and GPU temperature every second while a
game is active or the interface is visible, and keeps the highest temperature
reached during the game session. Hidden idle mode skips sensor polling. Green means Safe,
orange means Warm, and red means Danger. A sensor says Unavailable when Windows
or the laptop firmware does not expose it safely to a normal-user application.
If MSI Afterburner is already running, Game Management reads its CPU
temperature monitoring value through a read-only shared-memory connection.
It does not start profiles or change any MSI Afterburner setting.
When the final game process closes, a simple session summary appears. The game,
date, start/end time, total game time, timer cycles, and peak CPU/GPU temperatures are
also appended to GameSessionHistory.txt.

REPORTS, SEARCH, AND RECOVERY
-----------------------------
Reports shows weekly or monthly totals and lets you move to earlier or later
periods. Session history can be searched by date, mode, application, or shown
duration. Everything is read locally from GameSessionHistory.txt.

If the watcher or Windows stops unexpectedly, the next watcher start restores
the captured power plan and brightness and saves the interrupted session when
it lasted more than two minutes. Its unique session ID prevents duplicate
history entries.

PORTABLE BACKUP
---------------
Options > Backup data creates one .gmbak file containing settings and session
history. It does not include executables, logs, temporary runtime state, or
machine power settings. Pause Game Management before Restore data; the restored
data is then loaded while monitoring remains paused until you choose Enable.

The application can be closed to the notification area. Exiting the interface
does not stop the background watcher. Use Pause to stop Game Management.
Temperature sensors run in the background so slow hardware queries do not
delay button clicks. Mode and settings changes wake the watcher immediately.
Use Hide activity to collapse the Recent activity panel; Show activity restores
it without affecting logging.
Use Flowers on the Current status panel to flip to three animated pixel-art
gerberas. Use Status to flip back. The animation does not affect monitoring.

SAFETY
------
The interface keeps the existing AC-only behavior, brightness restoration,
power-plan restoration, recovery state, and exclusions.
It does not control vendor fan/thermal modes, BIOS settings, GPU drivers,
voltages, clocks, thermal limits, or game process priority. Temperature checks
are read-only.

FILES
-----
GameManagement.exe              Application
Source\GameManagement.cs        Source code
GameManagement.ico              Application, taskbar, and notification-area icon
GameManagement.ps1              Background controller
Pause-GameManagement.ps1        Safe pause/restoration helper
Tests\                          Local verification scripts
Documentation\                  Changelog, temperature guide, and local reports

Settings, saved timer state, history, logs, and restoration data stay beside the
application because the running controller reads those paths. Public release
archives include default settings and exclude personal session data.
WORK MODE

Choose Work mode beside Show activity to flip the complete interface. Add the
exact .exe files you want Work Management to monitor, such as Code.exe,
eclipse.exe, or chrome.exe, then choose Save apps. Selected work apps use the
focus and break timer but do not activate gaming power, brightness, or Escape
behavior. Choose Game mode to flip back. Auto mode switching can instead follow
the actual foreground executable. Unassigned apps do nothing, and a manual mode
choice remains in force until another assigned foreground app is used.

Session time shows the current Work session as HH:MM:SS. It is calculated from
the shared session start time instead of adding one second per screen refresh,
so a busy or minimized interface does not make the displayed time drift.

Work has its own focus and break durations and its own timer-state file. The
Game timer settings and state remain separate. Choose Installed apps... for
launchable Windows applications, or Browse file... for a portable application
or an app that Windows does not list. Launch selected starts the highlighted
executable. Reports summarizes saved sessions by day and mode, including weekly
and monthly periods and sessions that cross midnight. App time is shown only when one app was recorded
for a session. Sessions lasting two minutes or less remain excluded from the
saved history, as in earlier versions.

Use Pause in the Work status panel to freeze the exact Work or Break time left.
Use Resume to restart monitoring; the frozen countdown continues when a selected
Work application is detected again.
