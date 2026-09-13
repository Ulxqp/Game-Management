# Game Management changelog

## 2.0.2 — current

- Added strict single-instance protection for the main Game Management interface.
- Opening Game Management again now shows and activates the existing window instead of starting a duplicate.
- Kept timer-alert, session-summary, and preview windows independent so their intended dialogs still work.

## 2.0.1

- Disabled the timer **Confirm** button when the displayed timer settings already match the saved settings.
- Made **Confirm** enable immediately when the game timer, break timer, or timer on/off setting changes.
- Made **Confirm** return to its disabled state after either Confirm or Save settings successfully saves the values.

## 2.0.0

- Renamed the complete product identity to **Game Management**.
- Renamed the application, watcher, scripts, installer, icon, previews, logs, shortcuts, startup entry, uninstall entry, internal namespace, signals, and power plan.
- Changed the installation folder to `Documents\GameManagement` while preserving the existing settings, logs, history, and rollback backups during migration.
- Kept all Game Management behavior, safety limits, and low-overhead monitoring unchanged.

## 1.13.0

- Reduced idle tray overhead by suspending temperature and timer-display polling when no game is active and the interface is hidden.
- Reused process classifications between scans so unchanged background processes do not require repeated executable-path inspection.
- Replaced routine WMI AC checks with the lightweight Windows power-status API while keeping WMI as a fallback.
- Reduced idle brightness queries while still taking a fresh reading immediately before Game Management activates.
- Combined the interface's duplicate active-power-plan lookups into one query per visible refresh.
- Kept one-second CPU/GPU temperature updates during games and while the interface is visible.

## 1.12.1

- Moved the status indicator upward so it no longer overlaps the Mode row.
- Kept the existing Windows 98 layout and all Game Management behavior unchanged.

## 1.12.0

- Changed the application's core description from game performance control to **Game Management**.
- Removed automatic High and Above Normal process-priority changes from the watcher.
- Removed the Stop priority / Start priority button and the priority-disabled runtime switch.
- Kept AC-only power-plan performance, brightness control and restoration, game/break timers, targeted Escape, game detection, temperatures, session history, and crash cleanup.
- Setup safely restores any legacy priority snapshot left by an older version, but the new application never raises a game process priority.

## 1.11.0

- Added a Windows 98-style **Stop priority** button beside Refresh.
- Stop priority immediately restores the detected game's original process priority while keeping the power plan, brightness, timer, game detection, and temperature monitoring active.
- A persistent safety switch prevents the watcher from silently setting High priority again.
- The button changes to **Start priority**, which resumes automatic High priority on the next watcher scan.
- Removed the unfinished Hide overlay / Show overlay button and its RTSS control code.
- Exited game-process entries and SnowRunner's crash reporter are ignored so they cannot keep management mode or High priority active after the game closes.

## 1.10.0

- Added a read-only MSI Afterburner CPU-temperature source for systems whose Lenovo/Windows sensor interface is unavailable.
- Reads only the official `MAHMSharedMemory` monitoring entry for CPU temperature; it cannot apply profiles or change clocks, voltage, power limits, fans, or thermal controls.
- Validates the shared-memory signature, version, sizes, entry count, buffer bounds, source ID, and plausible temperature range before displaying a value.
- Retains the existing Lenovo and Windows temperature fallbacks when MSI Afterburner is not running.

## 1.9.3

- Matched Hide activity to the exact width, height, and horizontal alignment of Open log and Diagnostics at every supported window size.
- Kept all other main-interface controls unchanged.

## 1.9.2

- Replaced the secondary-dialog buttons' variable Windows focus border with a fixed Windows 98 raised/sunken border.
- Buttons keep the same border thickness while idle, focused, and clicked; clicking still gives the normal inset pressed effect.
- Kept the main interface unchanged.

## 1.9.1

- Restyled the timer alarm and game-session summary as consistent Windows 98 dialogs.
- Added the same navy title bar, gray surface, raised frame, inset status areas, and classic beveled buttons used by the main interface.
- Kept the main interface completely unchanged.
- Added render checks for both secondary dialogs and a safety assertion that prevents either window from silently returning to modern styling.

## 1.9.0

- Replaced CPU/GPU workload percentages with live temperatures and per-session peak temperatures.
- Added plain color conditions: green Safe, orange Warm, and red Danger, using conservative limits for this laptop's Intel Core i5-12450HX and RTX 3050 Laptop GPU.
- Added safe sensor fallbacks. Unsupported or permission-blocked CPU temperature sensors show Unavailable instead of using a privileged hardware driver.
- Session summaries and `GameSessionHistory.txt` now record peak temperatures in °C.
- Removed the permanent heavy default-button outline from the session-summary Close button while keeping Escape-to-close support.
- Added `TEMPERATURE-GUIDE.md` with the limits, meaning, and source links.

## 1.8.0

- Added an enabled-by-default option to press Escape on the detected game when a break starts and ends.
- Escape input is target-verified: the watcher selects the detected game window, brings it forward, verifies focus, and cancels safely if focus fails.
- The main Game Management interface now appears automatically at break start and hides to the notification area at break end.
- Added live CPU and GPU usage with per-game-session peak percentages, refreshed every second. Replaced by temperature monitoring in 1.9.0.
- Added a simple session summary when the final game process exits.
- Added `GameSessionHistory.txt`, recording the game, date, start/end time, total game time, timer cycles, and peak CPU/GPU use.
- Added safety checks for targeted Escape input, break-window signaling, live performance sampling, and session history.
- Matched the Hide activity button height and border spacing to the other action buttons.

## 1.7.0

- Replaced the one-time timer chime with a topmost alarm that repeats until **Stop alarm** is pressed.
- Rewrote the timer alerts in shorter, plain language: **Time for a break!** and **Break over!**
- Added friendly duration formatting, including seconds for short test alarms.
- Added safety checks that confirm the alarm timer stops and is disposed when the alert closes.
- Included the application source with installed copies so the local safety test can inspect the alarm implementation.
- Made package-only installer checks skip cleanly when the safety suite is run from the installed runtime folder.

## 1.6.0

- Added informational detection for NVIDIA GeForce RTX 20- and 30-series desktop, Ti, SUPER, and Laptop GPU names.
- Added a non-mutating compatibility self-test to the installer build.
- Made the AnyCPU installer adapt to optional power settings omitted by vendor firmware.
- Added a Balanced-plan fallback when the standard High Performance template is unavailable.
- Removed hardcoded watcher cleanup paths so redirected and localized Documents folders work correctly.
- Added a per-install `HARDWARE-COMPATIBILITY.txt` report containing detected display adapters and applied power capabilities.
- Confirmed GPU clocks, voltages, drivers, firmware, and NVIDIA settings remain untouched.
- Updated the packaged installer in `dist` automatically after every verified build.

## 1.5.0

- Added a user-selectable 1-240 minute game timer, enabled at 30 minutes by default.
- Starts the countdown when the first game launches and resets it when the final game closes.
- Added a live timer display and a timer alert that works when the interface is closed.
- Added a configurable break timer that automatically returns to a fresh game timer.
- Added separate phase, minutes-left, and cycle-number readouts with smooth, flicker-free updates.
- Added a raised, beveled Windows 98-style border around the application window.
- Standardized the title-bar button widths and added a cosmetic Windows 98-style `?` button.
- Replaced the application, taskbar, and notification-area artwork with the supplied pixel-art computer icon.
- Added a single-file, retro-styled Windows installer with upgrade-safe settings, shortcuts, and registered uninstall support.
- Added an explicit Windows system sound when either the game timer or break timer finishes.
- Replaced the standalone countdown row with the cycle number and moved the live `MM:SS` value beside Minutes left.
- Added a Confirm button that immediately saves and applies the selected game and break durations.
- Added a Hide activity/Show activity button that collapses the log panel and resizes the window.
- Reduced the timer Confirm control to the same standard height as the other buttons.
- Renamed the Game settings panel to Settings.
- Added a Windows 98-style panel flip between Current status and three animated pixel-art gerberas.
- Removed the GERBERA.EXE status strip so the animated flower panel fills the available space.
- Standardized the Open log and Diagnostics controls to the same 25-pixel height as the other buttons.

## 1.4.0

- Game Management now requires AC power and cannot activate while the laptop is unplugged.
- Unplugging during a game immediately restores the original power plan, brightness, and process priorities.
- Reconnecting AC while a game remains open allows a fresh, fully captured management session.
- Added exact pre-activation process-priority capture and restoration.
- Fixed the transient 0% brightness edge case by caching brightness while idle, before game launch transitions.
- Extended interrupted-run, reinstall, and undo recovery to restore saved process priorities.
- Added security checks for AC-only gating, full disable restoration, and stable brightness capture.

## 1.3.0

- Saves the built-in display brightness when a game starts and changes it to 75%.
- Restores the exact pre-game brightness after the final game closes.
- Includes brightness in interrupted-run recovery, reinstall recovery, and undo.
- Leaves unsupported external monitors unchanged and logs when brightness control is unavailable.
- Added a bounded brightness safety check to the security test.
- Verified built-in display control and restoration with a reversible 75% → 74% → 75% test.

## 1.2.0

- Re-checks game process priority throughout the gaming session.
- Reapplies High priority if a game resets itself to Normal during startup.
- Verifies that High priority actually remained applied before logging success.
- Uses Above Normal only as a safe fallback when High is unavailable.
- Confirmed the dedicated plan uses 100% AC minimum/maximum CPU state, Aggressive processor boost, active cooling, and PCIe link-state power saving off.
- Made reinstall idempotent: an active GameManagement plan is safely restored before setup, and an existing dedicated plan is updated instead of deleted.
- Installer now fails clearly if any required `powercfg` operation fails.
- Completed a real SnowRunner test: automatic activation, priority correction, and exact restoration to Balanced all passed.
- Completed a second real-game test with The Witcher 3; activation, repeated priority correction, and exact restoration to Balanced all passed.
- Added `LIVE-TEST-REPORT.md` with the observed timeline and final system condition.

## 1.1.0

- Removed all Lenovo fan and thermal-mode automation.
- Normalized executable paths, including Steam paths containing `\.\`.
- Added automatic discovery of every Steam library folder.
- Added a named single-instance lock to prevent duplicate watchers.
- Added active-plan verification after every power-plan switch.
- Added startup, priority, activation, restoration, and recovery logs.
- Kept generic folder detection with no per-game configuration.
- Kept High priority with Above Normal fallback; Realtime is never used.
- Kept exact restoration of the pre-game Windows power plan.

## 1.0.0

- Added generic Steam game detection and a dedicated Game Management power plan.
- Added automatic activation, restoration, startup, backup, and undo.
