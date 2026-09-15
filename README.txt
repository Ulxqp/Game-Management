GAME MANAGEMENT
=================

Game Management is a Windows 98-style desktop and notification-area controller
for automatic game performance sessions and user-selected work focus sessions.
Its data remains in Documents\GameManagement for upgrade compatibility.

RUN
---
Double-click GameManagement.exe.

INSTALLER
---------
Run GameManagement-Setup.exe to install or upgrade the application. Setup
preserves existing settings, creates Desktop and Start Menu shortcuts, starts
the background watcher, and adds Game Management to Windows Installed Apps.
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
Save settings  Saves brightness, scan interval, and game-library folders.
Add            Adds a launcher or standalone game-library folder.
Game Management does not change game process priority.

GAME TIMER
----------
When enabled, the selected game timer starts as soon as the first game is
detected. When it finishes, the configured break timer starts automatically.
At the end of the break, a fresh game timer starts, creating a repeating
game/break cycle while the game remains open. The dashboard shows the active
phase, an exact MM:SS value beside Minutes left, and the current cycle number.
The visible timer display is updated independently for smooth display. Confirm applies
new game and break durations immediately. If the interface is closed, the
background watcher still tracks the timers and opens a topmost alarm. The
Windows alert sound repeats until Stop alarm is pressed. The alert uses short
messages such as "Time for a break!" and "Break over!" Closing the final game
cancels and resets the cycle.

The timer alarm and game-session summary use the same Windows 98-style navy
title bar, gray background, raised border, inset panels, and beveled buttons as
the main interface. The main interface itself is unchanged.

BREAK CONTROL
-------------
The Press Escape option is enabled by default. At break start, Hardware
Squisher finds the detected game's real window, brings that window forward,
checks that it truly has focus, and only then presses Escape. The main
interface appears with the break countdown. At break end, the same safe check
presses Escape again and the main interface hides to the notification area.
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

The application can be closed to the notification area. Exiting the interface
does not stop the background watcher. Use Pause to stop Game Management.
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
GameManagement.cs               Source code
GameManagement.ico              Application, taskbar, and notification-area icon
GameManagement.ps1              Background controller
Pause-GameManagement.ps1        Safe pause/restoration helper
WORK MODE

Choose Work mode beside Show activity to flip the complete interface. Add the
exact .exe files you want Work Management to monitor, such as Code.exe,
eclipse.exe, or chrome.exe, then choose Save apps. Selected work apps use the
focus and break timer but do not activate gaming power, brightness, or Escape
behavior. Choose Game mode to flip back.
