# Mode-switch bug fix test report — 2026-09-27

This report covers the source build and isolated test copies. It does **not** claim that the currently installed `Documents\GameManagement` copy has been updated.

## Root cause reproduced before the fix

The installed log showed Work monitoring active from 17:42 to 18:45, followed by a pause and a new Game watcher, without a Work history entry. In an isolated UI render, a `runtime-state.json` with `Mode=work` made the Game tile say `GAME ACTIVE` and showed a Game timer starting. The old mode switch saved the destination setting before the pause script killed the outgoing watcher; that bypassed the session-history path.

## Fix verified in isolated runs

- Work → Game and Game → Work both completed through the request/ack handoff. Each outgoing timer file was removed and the destination mode created its own timer file.
- A Work session over two minutes was saved exactly once as `Mode: work`. A Game session over two minutes was saved exactly once as `Mode: game`. Short sessions remained excluded by the existing rule.
- Work → Game → Work and four rapid flips completed without a crash. A duplicate click during an animation did not start another flip.
- Switching during a Game break cleared the Game break state and started a fresh Work timer.
- With a Work runtime file present, the Game UI showed `Ready`, cycle `0`, and not `GAME ACTIVE`; the Work UI showed its own active timer and cycle `2`.
- An inactive watcher acknowledged a switch without creating a session. Closing the isolated UI while its watcher was active did not stop or clear the watcher session.
- A forced error after the atomic history commit caused one retry and only one history entry with that session ID.
- A watcher delayed before the transition lock caused a timeout and rejected flip. The old Game session ID, start time, timer deadline, and settings were unchanged; the old session was not split.
- A watcher delayed for 20 seconds while holding the transition lock still completed the switch after the interface's normal 15-second wait. The interface accepted the committed handoff rather than canceling it.
- An expired orphan request with no living requester was removed without stopping or restarting the active Game session.
- The request and acknowledgement are published by temporary-file moves, so the watcher does not act on a partially written request.
- A deliberately stale temperature peak was reset on a new runtime session; the old 120°C value was not carried forward.
- The Weekly Report parser loaded distinct Work and Game entries. Fixture sessions also confirmed correct mode totals and splitting at midnight and a week boundary.
- The final `Build-GameManagement.ps1` run succeeded, including all 47 existing security checks. The PowerShell watcher parsed successfully, and `git diff --check` found no patch errors.

## Limits

The installed copy was not replaced during these tests. A real user-driven mode flip in the installed app, behavior during a power loss or disk failure, and every possible Windows UI timing race remain unverified. In particular, a UI process crash after the watcher validates a request but before settings are written could leave the app in the old mode with a stopped session. An acknowledgement write failure after session teardown could split a session, and a watcher stalled while holding the handoff mutex could make the interface unresponsive. Those fault cases were not executed. The test copy stubbed power-plan and brightness writes, so those hardware-facing operations were not exercised by the mode-switch regression tests.
