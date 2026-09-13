# Hardware Squisher changelog

## 1.6.0 — current

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

- Hardware Squisher now requires AC power and cannot activate while the laptop is unplugged.
- Unplugging during a game immediately restores the original power plan, brightness, and process priorities.
- Reconnecting AC while a game remains open allows a fresh, fully captured boost session.
- Added exact pre-boost process-priority capture and restoration.
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
- Made reinstall idempotent: an active GameBoost plan is safely restored before setup, and an existing dedicated plan is updated instead of deleted.
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

- Added generic Steam game detection and a dedicated Game Boost power plan.
- Added automatic activation, restoration, startup, backup, and undo.
