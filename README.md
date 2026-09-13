# Hardware Squisher

Hardware Squisher is a Windows desktop Game Management utility with a deliberately retro Windows 98 interface. It detects games in configured library folders, applies a dedicated AC-only performance plan, controls supported built-in display brightness, manages game/break timers, and restores the previous system state when the game closes. It does not change game process priority.

> **Notice:** This code was created entirely by AI. The application is intended for personal use only.

![Hardware Squisher interface](docs/HardwareSquisher-preview.png)

## Features

- Automatic game detection across configured folders and Steam libraries
- AC-power-only activation
- Dedicated Windows power plan with exact plan restoration
- No game process-priority changes
- Configurable gaming brightness with restoration
- Repeating game and break timers with a visible, repeating alarm and cycle counting
- Consistent Windows 98 styling across the main window, timer alarm, session summary, and installer
- Optional automatic Escape key at break start/end, sent only to the verified game window
- Automatic dashboard show during breaks and hide when play resumes
- Live CPU/GPU temperatures, color conditions, and per-session temperature peaks
- Read-only CPU-temperature integration with an already-running MSI Afterburner
- A saved session history with game, date/time, duration, cycles, and CPU/GPU peaks
- Taskbar and notification-area controls
- Collapsible activity log
- Animated Windows 98-style pixel-gerbera panel
- Single-file graphical installer and registered uninstall support

## Hardware compatibility

The universal Windows installer supports desktop and laptop PCs using NVIDIA GeForce RTX 20- or 30-series graphics, including Ti, SUPER, and Laptop GPU variants. RTX detection is informational. On NVIDIA systems, the dashboard uses a read-only `nvidia-smi` temperature query; Hardware Squisher does not change NVIDIA drivers, clocks, voltages, firmware, thermal targets, or vendor performance modes.

Setup uses the current user's resolved Documents folder, falls back to a compatible Windows power-plan base when needed, and skips optional power settings that a PC's firmware does not expose. The application can also run with other GPU brands; built-in display brightness control remains optional and unsupported external monitors are left unchanged.

## Install

Download and run [`HardwareSquisher-Setup.exe`](dist/HardwareSquisher-Setup.exe). Windows may show an Unknown publisher warning because the executable is not digitally signed.

The installer places the application in `Documents\GameBoost`, creates Desktop and Start Menu shortcuts, starts the watcher, and adds Hardware Squisher to Windows Installed Apps. Existing settings are preserved during upgrades.

## Safety

Hardware Squisher does not control process priority, fan firmware, BIOS settings, voltages, thermal modes, thermal limits, or CPU/GPU clocks. Its temperature monitor is read-only. On this Lenovo, CPU temperature can be read from MSI Afterburner's monitoring data while MSI Afterburner is running; otherwise Lenovo/Windows fallbacks are tried and Unavailable is shown. Hardware Squisher only activates on AC power. Pausing or uninstalling restores the captured power plan and brightness. Setup also restores a legacy priority snapshot if an older version left one behind during an interrupted session.

## Build from source

On 64-bit Windows with .NET Framework 4 installed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-HardwareSquisher.ps1
```

This compiles the AnyCPU application, runs the safety and RTX compatibility tests, and creates the single-file installer both one directory above the source folder and in `dist`.

## Verification

The v1.12.1 package was verified on September 14, 2026 before publication. The status indicator stays clear of the Mode row, and the Game Management watcher contains no process-priority controls. When a timer ends, a topmost plain-language Windows 98-style alert repeats the Windows alarm sound until **Stop alarm** is pressed. Secondary-dialog buttons use a fixed classic border so focus cannot add an inconsistent heavy edge. Hide activity aligns with Open log and Diagnostics. MSI Afterburner CPU temperature access is read-only. The timer cycle itself continues in the background.

- AnyCPU application and installer compilation: **PASS**
- PowerShell safety, sensor-access, portability, and interface-alignment suite: **29/29 checks passed**
- Embedded installer payload verification: **PASS** (exit code 0)
- RTX 20/30 compatibility classifier: **PASS** (exit code 0)
- Non-installing setup-window render smoke test: **PASS** (exit code 0)
- Root and `dist` installer copies: **identical**

The classifier test covers representative RTX 2060, 2070 SUPER, 2080 Ti, 3050 Laptop, 3060 Ti, 3070, 3080 Laptop, and 3090 names. This verifies detection and hardware-independent setup behavior; it is not a claim that the application was physically tested on every GPU model or PC configuration.

## Screenshots

![Animated pixel gerberas](docs/HardwareSquisher-flowers-preview.png)

![Collapsed activity panel](docs/HardwareSquisher-collapsed-preview.png)

## Project files

- `HardwareSquisher.cs` — desktop interface
- `HardwareSquisher.ps1` — background game watcher and timer controller
- `Install-HardwareSquisher.ps1` — installation/configuration helper
- `Pause-HardwareSquisher.ps1` — safe pause and restoration helper
- `Undo-HardwareSquisher.ps1` — full restoration helper
- `Uninstall-HardwareSquisher.ps1` — Windows uninstall workflow
- `HardwareSquisherSetup.cs` — single-file graphical installer

See [CHANGELOG.md](CHANGELOG.md) for update history.
