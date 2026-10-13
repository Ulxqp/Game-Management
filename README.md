# Game Management 5.0.2

Game Management is a Windows desktop controller for independent Work and Game focus timers, application-aware mode detection, break reminders, temperature monitoring, tasks, calendar ranges, and activity reports.

## Highlights

- Modern React/TypeScript desktop interface backed by the retained .NET Framework and PowerShell engine.
- Independent Work and Game timers with exact-second pause and resume checkpoints.
- Click-to-dock Calendar and Tasks panels, with temporary hover previews and saved pin preferences.
- Custom local WAV alarm selection with preview and safe Windows-sound fallback.
- Real executable icons, recorded session statistics, filtered reports, and CPU/GPU temperature display when sensors are available.
- AC-only performance management that remains disabled while the laptop is unplugged.

## Download

Use `GameManagement-Setup.exe` from the latest GitHub release. Upgrades preserve compatible settings, history, timer state, tasks, and panel preferences. The portable ZIP includes the application, source, tests, and documentation with clean default settings.

## Repository layout

- `Source/` — native application, installer, and release build scripts.
- `Tests/` — security, feature, timer, integration, and panel regressions.
- `frontend/` — React/TypeScript interface.
- `bridge/` — local WebView2 desktop bridge.
- `Documentation/` — design, implementation, compatibility, changelog, and test reports.
- `references/` — approved visual targets and user-provided references.

Build with Windows PowerShell in STA mode:

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File .\Source\Build-GameManagement.ps1
```

See [Documentation/README.txt](Documentation/README.txt) and [Documentation/CHANGELOG.md](Documentation/CHANGELOG.md) for full details.
