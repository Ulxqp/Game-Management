# Game Management security report

Generated: 2026-10-13 21:46:30 +08:00

Overall result: **PASS**

| Check | Result | Detail |
|---|---:|---|
| PowerShell syntax | PASS | All scripts parse successfully. |
| No dangerous scripting primitives | PASS | No download, eval, security-disable, boot, disk, or recovery-deletion commands found. |
| No network access | PASS | No web, socket, download, or upload code found. |
| No fan or firmware control | PASS | Fan mode, firmware, BIOS, voltage, and clocks are untouched. |
| Complete Game Management identity | PASS | Application scripts contain no previous product, executable, service, or data-folder identity. |
| No game process priority changes | PASS | The Game Management watcher never raises, lowers, or repeatedly resets a game process priority. |
| Single-instance protection | PASS | Duplicate watchers exit immediately. |
| Single interface instance | PASS | Only one main interface can run; a background listener visibly raises the hidden window after later launches without weakening break-screen behavior. |
| Normalized generic detection | PASS | Steam paths and libraries are detected without game-specific entries. |
| Cached process classification | PASS | Unchanged processes reuse their safe game/non-game classification instead of reopening every executable path each scan. |
| Exact restoration | PASS | The pre-game plan is captured and restored. |
| Bounded brightness restoration | PASS | Built-in display brightness is bounded to 0-100, captured before gaming, and restored by normal shutdown, recovery, reinstall, and undo. |
| Stable brightness capture | PASS | Brightness is sampled immediately before activation and otherwise refreshed at a low idle rate. |
| AC-only activation guard | PASS | Game Management cannot activate on battery and disables itself when AC is disconnected. |
| Fast AC status path | PASS | The normal AC check uses the lightweight Windows power-status API while retaining the prior WMI fallback. |
| Full disable restoration | PASS | Power plan and brightness are captured and restored on normal disable and recovery. |
| Timer alarm stops safely | PASS | The visible timer alarm repeats a Windows sound until Stop alarm is pressed, then stops and disposes its timer. |
| Plain-language timer alert | PASS | Timer messages use short, plain-language instructions. |
| Timer confirmation change tracking | PASS | Confirm stays disabled for saved timer values, enables when a timer setting changes, and resets after a successful save. |
| Shared modern secondary dialogs | PASS | Alarm and summary dialogs retain their shared base while using the centralized v5 theme and Windows font. |
| Centralized v5 design tokens | PASS | The shared theme defines consistent colors, Windows typography, spacing, control heights, and card radius. |
| React desktop is the installed default | PASS | The installer exposes only the React desktop app in Start and removes the obsolete duplicate shortcut while retaining the native executable for recovery. |
| Bounded loopback desktop bridge | PASS | The embedded UI uses a loopback-only TCP listener with bounded HTTP input and I/O timeouts, request-local JSON serialization, same-origin POST checks, and static-path containment. |
| Allowlisted application icon bridge | PASS | Real Windows application icons are served only for exact executable paths already saved in Work or Game settings, with a local UI fallback when extraction fails. |
| Bounded local custom alarm | PASS | Custom alarms accept only bounded local WAV files, remain opt-in through the typed loopback bridge, stop with their owning UI, and retain the Windows alarm fallback. |
| Serialized recoverable React snapshot polling | PASS | React allows only one snapshot request at a time, times out and retries stalled requests, ignores stopped pollers, and keeps the last valid snapshot visible with a compact delayed-connection status. |
| Isolated preview is read-only | PASS | Preview mode rejects every non-GET bridge request and retains a command-layer guard before native launch, pause, resume, or reset can run. |
| Pause marker changes only after transactional pause | PASS | React and native callers delegate marker removal to the transactional pause script, which restores the engine marker and original startup values if cleanup fails. |
| React snapshot preserves paused elapsed sessions | PASS | The desktop snapshot exposes mode-specific paused session checkpoints; React prefers a committed pause over same-session stale runtime and uses exact active intervals for reports. |
| Integrated Windows desktop chrome | PASS | The WebView host has one React-rendered Windows title bar with working drag, resize, minimize, maximize/restore, and close controls. |
| Typed independent overlay preferences | PASS | Calendar and Tasks pin independently through a bounded Version 1 bridge document; the React interface docks clicked panels beside the page while hover previews remain temporary. |
| React timer duration and report controls | PASS | React source exposes bounded H/M/S editors and four real report periods using the native Monday week boundary. |
| Embedded five-destination navigation | PASS | The application contains its top navigation for all five pages and explicit keyboard handling. |
| Independent Calendar and Tasks overlays | PASS | Calendar and Tasks provide independent panel pinning, actual date ranges, and local persistent task storage. |
| Shared Game and Work dashboard behavior | PASS | Both dashboards use the same v5 design system while retaining their independent mode-switching and timer refresh paths. |
| User-selected work applications | PASS | Work mode monitors only executable files explicitly selected by the user and matches their exact paths. |
| Work profile avoids gaming power changes | PASS | Selected work apps receive focus timing without gaming power-plan, brightness, or Escape behavior. |
| Pause-aware Work session timer | PASS | Work Mode displays accumulated active time plus only the running interval, keeping HH:MM:SS frozen while paused. |
| Target-verified Escape control | PASS | Escape is sent only after the detected game window is selected and verified as the foreground window; there is no global fallback. |
| Timer dialog only at timer transitions | PASS | Timer transitions open only the alarm dialog and do not show or hide the main interface. |
| Independent Work timer settings and state | PASS | Work and Game use separate saved settings and timer-state files while sharing the countdown controller. |
| Pause freezes and resumes timer state | PASS | Game and Work Pause store the phase, cycle, and remaining seconds; Resume preserves that state until the matching application returns, and the dashboard displays the frozen value. |
| Installed app selection retains file browse | PASS | Installed apps can be chosen and launched; the existing executable picker remains available. |
| Game installed-app detection uses exact paths | PASS | Game Mode can select installed or manually browsed executables by exact path while retaining folder detection. |
| Temperature polling does not block interface controls | PASS | Hardware temperature queries run in one bounded background worker and return results to the interface thread. |
| Settings handoff wakes watcher immediately | PASS | Mode switches and settings saves signal the watcher instead of waiting for the next scan interval. |
| Weekly report from existing session history | PASS | Weekly totals are split at midnight and week boundaries from recorded start/end times. |
| Monthly reports and searchable history | PASS | Reports include weekly and monthly periods, and saved sessions can be filtered locally by date, mode, app, or duration. |
| Portable data backup is bounded and local | PASS | Backup and restore handle only named settings/history entries, require a fully paused engine, enforce size limits, roll back failed replacement, and never extract arbitrary archive paths. |
| Configurable local notifications | PASS | Timer popup, alarm sound, and end-of-session summary choices are local settings with safe enabled defaults. |
| Interrupted session recovery | PASS | Unexpected watcher shutdowns preserve eligible sessions without showing a stale popup, and SessionId prevents duplicate history entries. |
| Read-only CPU and GPU temperatures | PASS | CPU and GPU temperatures and session peaks refresh every second using read-only queries, with color and unavailable handling. |
| Idle tray sensor suspension | PASS | Hidden idle operation skips GPU, CPU, and timer display polling, removes stale session snapshots, and keeps active monitoring real time. |
| Single power-plan status query | PASS | A visible status refresh parses one powercfg result instead of launching powercfg twice, while runtime stateâ€”not the selected planâ€”owns the active label. |
| Read-only MSI Afterburner CPU sensor | PASS | MSI Afterburner CPU temperature is consumed through its existing monitoring map with read-only handles and bounded data validation. |
| Priority controls removed from interface | PASS | The Game Management interface and watcher contain no process-priority feature. |
| Exited game cleanup | PASS | Exited process entries and known crash reporters are ignored so management mode can restore after the real game closes. |
| Persistent game session notes | PASS | A plain-text history and visible exit summary store the game, date/time, duration, cycles, and peak CPU/GPU temperatures. |
| Short sessions excluded from history | PASS | Sessions lasting exactly two minutes or less do not update history or open a saved-session summary. |
| Non-blocking status collection | PASS | Power, brightness, watcher, process, and Work-app status checks run in one coalesced background worker. |
| Stale status results rejected | PASS | A delayed status result cannot overwrite a newer Game/Work mode or settings state. |
| Non-blocking control and settings changes | PASS | Settings, mode handoffs, Enable, Pause, and startup changes leave the Windows message loop responsive and replace settings atomically. |
| Serialized watcher transitions | PASS | Status restart holds the same gate as save/control actions, rechecks state before launch, and all user Exit paths wait for transitions. |
| Background installed-app discovery | PASS | The app picker opens before registry, shortcut, and icon discovery completes, and caches discovery results for later opens. |
| Bounded shared-log reading | PASS | Status refresh reads only a bounded log tail and safely shares the file with the background watcher. |
| Normal-user startup only | PASS | No service or elevated startup mechanism is used. |
| Single configurable application startup | PASS | One per-user Run entry launches the single-instance application hidden, and watcher restarts and upgrades preserve the startup and paused choices. |
| RTX 20/30 compatibility coverage | PASS | Installer build files are intentionally omitted from the installed runtime; this package-only check was completed before installation. |
| GPU-independent behavior | PASS | Installed runtime scripts contain no vendor-specific GPU tuning commands. |
| Adaptive Windows power settings | PASS | Setup falls back to Balanced and skips unsupported optional power settings on vendor-specific firmware. |
| Redirected Documents compatibility | PASS | Watcher cleanup uses the resolved installation path, including redirected or localized Documents folders. |
| Foreground-only automatic mode detection | PASS | Automatic switching classifies only the active window once per second, ignores unassigned apps, and keeps a manual override until the foreground assignment changes. |
| Authenticated timer reset channel | PASS | Timer reset commands are local, short-lived, tied to the requesting process instance, and acknowledged before the interface reports success. |
| Independent persistent completed cycles | PASS | Game and Work completed-cycle totals persist separately, increment at the end of a break, and session history records only the cycles completed during that session. |
| Mode handoff preserves independent timers | PASS | A mode change freezes the outgoing countdown and elapsed session without finalizing history, preserves both modes, and keeps one watcher. |
| Bounded second-resolution durations | PASS | All four timers use bounded seconds with legacy-minute fallback and interruptible deadline wake-ups. |
| Pause-aware history accounting | PASS | Elapsed session checkpoints and history use active time, excluding paused gaps from calendar reports. |
| No elevated Game Management task | PASS | No elevated task is installed. |

## SHA-256 hashes

```text
8963039A451061CAF73CC0979E21AC9ECB65E20F3CFF247F15396E8F0A166C8C  GameManagement.ps1
8C5FC2DAA7480868518A3C6EED7C5D4D4711DA72BDA41CA9DB73427B81843FA6  Install-GameManagement.ps1
36FE8361154BCC2B8AFB97242911AEEE837A361A5D0CEDB163FEACC61F7EB08F  Pause-GameManagement.ps1
75C378FCEC267A9377821E238D65634FFE755169AF526998A3EA3502D26B7D50  Undo-GameManagement.ps1
D07D20683664379609E46F8DAC3CFF6022F4B5E6C69DBEB2D9473994C4FBF999  Uninstall-GameManagement.ps1
```
