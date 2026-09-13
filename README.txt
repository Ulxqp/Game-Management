HARDWARE SQUISHER
=================

Hardware Squisher is a Windows desktop and notification-area controller for
the existing performance-mode data folder in Documents\GameBoost.

RUN
---
Double-click HardwareSquisher.exe.

INSTALLER
---------
Run HardwareSquisher-Setup.exe to install or upgrade the application. Setup
preserves existing settings, creates Desktop and Start Menu shortcuts, starts
the background watcher, and adds Hardware Squisher to Windows Installed Apps.
Uninstalling safely restores captured Windows settings and keeps the settings
and activity log in Documents\GameBoost.

Build-HardwareSquisherInstaller.ps1 rebuilds the single-file installer after
the application or its packaged files are updated.

CONTROLS
--------
Enable         Installs/starts Hardware Squisher and enables sign-in startup.
Pause          Stops the watcher, restores captured system settings, and
               disables sign-in startup without deleting the power plan.
Save settings  Saves brightness, scan interval, and game-library folders.
Add            Adds a launcher or standalone game-library folder.

GAME TIMER
----------
When enabled, the selected game timer starts as soon as the first game is
detected. When it finishes, the configured break timer starts automatically.
At the end of the break, a fresh game timer starts, creating a repeating
game/break cycle while the game remains open. The dashboard shows the active
phase, an exact MM:SS value beside Minutes left, and the current cycle number.
The timer display is updated independently for smooth display. Confirm applies
new game and break durations immediately. If the interface is closed, the
background watcher still tracks the timers, plays the Windows exclamation
sound, and opens each alert. Closing the final game cancels and resets the
cycle.

The application can be closed to the notification area. Exiting the interface
does not stop the background watcher. Use Pause to stop Hardware Squisher.
Use Hide activity to collapse the Recent activity panel; Show activity restores
it without affecting logging.
Use Flowers on the Current status panel to flip to three animated pixel-art
gerberas. Use Status to flip back. The animation does not affect monitoring.

SAFETY
------
The interface keeps the existing AC-only behavior, High priority fallback,
brightness restoration, power-plan restoration, recovery state, and exclusions.
It does not control Lenovo fan/thermal modes, BIOS settings, voltages, or clocks.

FILES
-----
HardwareSquisher.exe              Application
HardwareSquisher.cs               Source code
HardwareSquisher.ico              Application, taskbar, and notification-area icon
HardwareSquisher.ps1              Background controller
Pause-HardwareSquisher.ps1        Safe pause/restoration helper
