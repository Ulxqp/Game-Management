# Game Management v5.0.2

Version 5.0.2 introduces the modern Game Management desktop interface while preserving the existing Windows timer engine, saved data, AC-only safeguards, and native fallback.

## What’s new

- Added a React/TypeScript desktop interface with five top navigation destinations and one consistent Start Menu application.
- Added click-to-dock Calendar and Tasks panels. Hover previews remain temporary, and saved pins restore across restarts.
- Added selectable local WAV alarms with preview, reset-to-default, a 25 MB local-file limit, and automatic Windows-sound fallback.
- Added real executable icons for selected Work and Game applications.
- Added hours, minutes, and seconds to all Work, Game, and break timer settings.
- Preserved each mode’s remaining timer and active session time when paused or switched.
- Added accurate date-filtered reports, task persistence, application management, and current/peak temperature display when sensors are available.
- Fixed the two-second countdown jump, stalled snapshot refreshes, duplicate application instances, and compact Options-page overlap.
- Kept management disabled on battery power and retained safe restoration of brightness and power settings.

## Downloads

- **GameManagement-Setup.exe** — recommended installer or upgrade; preserves compatible user data.
- **GameManagement-v5.0.2.zip** — portable application with clean defaults, source, tests, and documentation.
- **SHA256SUMS.txt** — SHA-256 checksums for both release packages.

## Verification

- Security checks: 78 passed.
- Feature checks: 23 passed.
- Timer lifecycle checks: 25 passed.
- Version 5 checks: 22 passed.
- Integration checks: 23 passed.
- Panel integration checks: 46 passed.
- Installer payload and portable archive were rebuilt and verified before upload.

Windows and .NET Framework 4 are required. The executable is unsigned. CPU temperature availability depends on sensors exposed by the computer; unavailable readings remain labeled as unavailable.
