# Game Management feature test report

Generated: 2026-10-13 21:46:39 +08:00

Overall result: **PASS**

| Test | Result | Detail |
|---|---:|---|
| Duration units and migration | PASS | Legacy minutes migrate; seconds take precedence; values stay within 1 second to 24 hours and show hours correctly. |
| Elapsed session pause and resume | PASS | A one-hour pause adds no elapsed time; resume adds only the new running interval. |
| Persisted elapsed clock mode isolation | PASS | Game and Work load their own frozen elapsed time even when runtime-state has been removed by Pause. |
| Committed pause beats stale runtime | PASS | A committed frozen checkpoint wins over leftover runtime for the same session. |
| Stale pause cannot replace newer session | PASS | An older paused session ID cannot freeze or replace a different active runtime session. |
| History parser | PASS | Three saved sessions, including cross-boundary sessions, were loaded. |
| Monthly boundary totals | PASS | October includes only the October portions of sessions that cross into or out of the month. |
| Weekly boundary totals | PASS | The final October session is included correctly in its Monday-to-Sunday week. |
| Foreground application classification | PASS | Exact Game/Work assignments and game-library folders classify correctly; unassigned and conflicting entries do not trigger a switch. |
| Hours minutes seconds editor | PASS | All three independently labeled fields round-trip a mixed-unit duration. |
| Paused report calendar attribution | PASS | Reports split active intervals at the week boundary and exclude the one-hour paused gap. |
| Reports and history interface | PASS | The dialog exposes Reports and Session history and lists all fixture sessions. |
| Reports dialog render | PASS | The Windows 98-style report dialog renders without an exception. |
| Notification defaults | PASS | Popup, sound, and summary notifications default to enabled, including when an upgraded settings file omits the new fields. |
| Automatic mode detection safe default | PASS | Automatic mode detection is opt-in so an upgrade does not unexpectedly change modes. |
| Suppressed timer notification | PASS | With popup and sound disabled, the alert code returns before opening a dialog or playing a sound. |
| Custom alarm fallback | PASS | A valid custom WAV loops while the alarm is visible; missing or unreadable custom audio falls back to the Windows alarm, including sound-only alerts. |
| Timer pause freezes exact state | PASS | The real pause-state writer should preserve Work/cycle 3/about 125 seconds. Observed exit=0 paused=True phase=Work cycle=3 seconds=125 log=2026-10-13 21:46:32 Timer paused with 125 seconds left in Work, cycle 3. |
| Timer resumes frozen remainder | PASS | The real watcher must resume the frozen Work timer without a fresh countdown. Exit=0; log=2026-10-13 21:46:32 Timer paused with 125 seconds left in Work, cycle 3
2026-10-13 21:46:33 Watcher started in AC-only work mode; cached idle brightness: 65%; monitoring: C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe
2026-10-13 21:46:34 Work timer resumed with 125 seconds left in cycle 3
2026-10-13 21:46:34 Work Management ON; applications: powershell, powershell
2026-10-13 21:46:34 Work Management OFF; selected applications closed
2026-10-13 21:46:34 Work session not saved because it lasted 2 minutes or less (1 sec)
 |
| Completed cycle increments exactly once | PASS | A completed Work break increments Work cycles from 0 to 1, and a later watcher pass cannot duplicate the increment. |
| Independent reset-all command | PASS | Reset All clears only the selected Work timer/counter and preserves the Game completed-cycle total. |
| Interrupted session runtime recovery | PASS | An isolated three-minute interrupted Work session is restored to history once, even when the same recovery state is replayed. |
| Crash recovery deduplication | PASS | Recovered sessions reuse the normal unique-ID history writer. |
