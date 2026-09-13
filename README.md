# Hardware Squisher

Hardware Squisher is a Windows desktop utility with a deliberately retro Windows 98 interface. It detects games in configured library folders, applies a dedicated AC-only performance plan, raises game process priority safely, controls supported built-in display brightness, and restores the previous system state when the game closes.

![Hardware Squisher interface](docs/HardwareSquisher-preview.png)

## Features

- Automatic game detection across configured folders and Steam libraries
- AC-power-only activation
- Dedicated Windows power plan with exact plan restoration
- High process priority with Above Normal fallback; Realtime is never used
- Configurable gaming brightness with restoration
- Repeating game and break timers with audible alerts and cycle counting
- Taskbar and notification-area controls
- Collapsible activity log
- Animated Windows 98-style pixel-gerbera panel
- Single-file graphical installer and registered uninstall support

## Install

Download and run [`HardwareSquisher-Setup.exe`](dist/HardwareSquisher-Setup.exe). Windows may show an Unknown publisher warning because the executable is not digitally signed.

The installer places the application in `Documents\GameBoost`, creates Desktop and Start Menu shortcuts, starts the watcher, and adds Hardware Squisher to Windows Installed Apps. Existing settings are preserved during upgrades.

## Safety

Hardware Squisher does not control fan firmware, BIOS settings, voltages, thermal modes, or CPU/GPU clocks. It only activates on AC power. Pausing or uninstalling restores the captured power plan, brightness, and process priorities.

## Build from source

On 64-bit Windows with .NET Framework 4 installed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-HardwareSquisher.ps1
```

This compiles the application, runs the safety checks, and creates the single-file installer one directory above the source folder.

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

