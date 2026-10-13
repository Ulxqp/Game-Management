# Timer ownership and state graph

Version 3.3.0. This graph is the design and regression-check map, not an extra runtime component.

```text
Game H/M/S controls ----> gameTimerSeconds + breakTimerSeconds --+
Work H/M/S controls ----> workTimerSeconds + workBreakSeconds ---+--> one watcher
Legacy minute settings --> seconds fallback -------------------+
                                                               |
                     +-----------------------------------------+
                     v
             selected mode owns running state
                     |
           +---------+----------+
           v                    v
  phase countdown          session elapsed clock
  deadline - now           accumulated + running interval
           |                    |
  game/work-timer-state     game/work-session-state
           |                    |
  remaining seconds        elapsed seconds + original ID + active intervals
           |                    |
           +---------+----------+
                     v
                UI and history
```

```text
Ready -- assigned app detected --> Running session / focus phase
Running -- focus expires -------> Running session / break phase
Break -- break expires ---------> Running session / focus phase; completed cycles +1
Running -- Pause ---------------> Paused: remaining and elapsed saved, clock stopped
Paused -- Resume + app present -> Running: same ID, elapsed baseline, new running interval
Running -- mode handoff --------> outgoing mode paused; incoming mode resumes independently
Running -- app closes ---------> finalize active time once; clear that mode's session
```

Pause transaction: disable automatic restart -> verify the owned watcher stopped -> commit session and countdown checkpoints -> restore prior system settings -> remove live runtime state -> report success. Required failures are reported, not shown as a successful pause. Logging is best-effort and is not the checkpoint commit boundary. If cleanup is interrupted, a paused checkpoint overrides stale runtime only when the session IDs match.

## Invariants

- Durations use integer seconds, from one second through 24 hours. Hours, minutes, and seconds are presentation fields.
- Only one watcher owns the live countdown. Game and Work saved states remain separate.
- A paused countdown uses saved remaining seconds, never an old deadline. A paused elapsed clock adds no wall time.
- Resume preserves the session ID and original start, adding only new active intervals.
- Reset Timer resets the selected countdown, not the accumulated elapsed session or the other mode.
- Completed cycles change only after a whole focus/break cycle completes.
- History records active seconds and active intervals so paused gaps are excluded from daily, weekly, and monthly totals.
- Old settings and history without new fields retain their existing meaning.
- The existing rule excluding sessions of two minutes or less applies to active time, not paused wall time.

## Code edges

- `DurationInput` / `TimerDuration` -> `GameSettings` -> watcher duration resolution.
- Watcher `Start-Management` / `Stop-Management` -> session persistence and history.
- Pause helper -> frozen countdown and frozen session -> runtime cleanup.
- `SessionClock` -> Work Mode elapsed display; UI refresh does not own the elapsed counter.
- `WeeklyReport` -> interval slicing at local calendar boundaries, with legacy-history fallback.

Regression coverage checks mixed-unit durations, migration, pause/resume, mode isolation, elapsed accounting, and reports across calendar boundaries.
