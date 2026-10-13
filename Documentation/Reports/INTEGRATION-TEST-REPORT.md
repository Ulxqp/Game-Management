# Version 5 UI integration verification

Result: **PASS**. 23 passed, 0 failed.

Invocation: Nested script invocation (build compatible).

Verified executable SHA-256: d486a3c90a4f2086d88db227d70f011921e2f03c82bc4b6deeb85ca88197b572

The harness creates a temporary preview root containing anonymous test settings and recorded-session fixtures. Every constructed MainForm and its settings, history, task and panel-preference write targets must resolve to that scratch root. Every direct fixture write is checked before writing; unique fixture markers are checked against readable installed data snapshots. It does not enable or pause the live watcher, change startup behavior, or change Windows display settings.

Installed settings, history, tasks and panel-preference hashes are recorded before and after as diagnostics. The user may run the installed application during the suite. A concurrent installed-file change is reported as external state, without claiming that the file remained unchanged or attributing its writer. A tested form resolving outside scratch, or a unique fixture marker appearing in installed data, fails isolation.

The 100%, 125% and 150% checks simulate WinForms control scaling and larger fonts. The viewport is limited to the current native maximum window size; larger content must remain reachable by scrolling. They are not a substitute for testing per-monitor DPI transitions on another display. Reduced motion is checked against the current Windows animation preference; that preference is never changed.

| Check | Result | Evidence |
|---|---|---|
| Preview isolates every live timer, watcher and data root | PASS | Verified on isolated fixture data. |
| Modern dashboard refresh survives detached controls and form teardown | PASS | Verified on isolated fixture data. |
| Normal status snapshots refresh Game and Work UI without enabling live workers | PASS | Verified on isolated fixture data. |
| Stopped watcher disables Pause and enables Resume on both dashboards | PASS | Verified on isolated fixture data. |
| Five destinations share one title bar at 800, 960 and 1280 widths | PASS | Verified on isolated fixture data. |
| Both Home dashboards fit compact timer, applications and lower cards at 1280 by 880 | PASS | Verified on isolated fixture data. |
| Unavailable telemetry is truthful on both modes | PASS | Verified on isolated fixture data. |
| A small Home window stacks lower cards and offers scrolling | PASS | Verified on isolated fixture data. |
| Calendar custom range updates Reports and both real Home overviews | PASS | Verified on isolated fixture data. |
| Calendar and overview View reports actions carry their selected dates | PASS | Verified on isolated fixture data. |
| Calendar week uses current culture and Today resets linked ranges | PASS | Verified on isolated fixture data. |
| Expanded Calendar and Tasks stack as overlays without shifting Home | PASS | Verified on isolated fixture data. |
| Focused Tasks remains open until focus leaves then collapses safely | PASS | Verified on isolated fixture data. |
| Preview pins do not write preferences; persisted pins restore independently | PASS | Verified on isolated fixture data. |
| Restore defaults remains pending until Save and Discard reloads exact seconds | PASS | Verified on isolated fixture data. |
| Timer sound and time-up options synchronize and enable Save only for pending changes | PASS | Verified on isolated fixture data. |
| Native alerts expose Save Discard Cancel and Remove Keep with keyboard bindings | PASS | Verified on isolated fixture data. |
| Application removal uses the actual Remove Keep alert and only changes selection | PASS | Verified on isolated fixture data. |
| Hover transitions settle and honor the current reduced-motion preference | PASS | Verified on isolated fixture data. |
| WinForms scaling simulation at 100 percent keeps navigation and timer controls reachable | PASS | Verified on isolated fixture data. |
| WinForms scaling simulation at 125 percent keeps navigation and timer controls reachable | PASS | Verified on isolated fixture data. |
| WinForms scaling simulation at 150 percent keeps navigation and timer controls reachable | PASS | Verified on isolated fixture data. |
| Every tested form and fixture write stays in scratch without leaking fixture markers to installed data | PASS | Verified 5 form roots and 3 fixture write paths. Unique fixture markers were absent from readable installed data snapshots. Installed hashes are diagnostic observations. |

Installed data hash diagnostics:

| File | Before SHA-256 | After SHA-256 | Observation |
|---|---|---|---|
| settings.json | 0e83b57bfd97f704271d87b6507d9c5db0ac59180c50ae0256156ee8b895f076 | 0e83b57bfd97f704271d87b6507d9c5db0ac59180c50ae0256156ee8b895f076 | Same observed hash |
| GameSessionHistory.txt | b7c2a073bc70a8d15eb57727526f66037dca20cb9561141937ab9c908463e861 | b7c2a073bc70a8d15eb57727526f66037dca20cb9561141937ab9c908463e861 | Same observed hash |
| tasks.json | Missing or unreadable | Missing or unreadable | Same observed hash |
| panel-preferences.json | 299d4bbbedfc2feae8ecad693b7e0fa1161b1c82e456b3d44424d62c01bec4f4 | 299d4bbbedfc2feae8ecad693b7e0fa1161b1c82e456b3d44424d62c01bec4f4 | Same observed hash |

No installed-file hash change was observed between the diagnostic snapshots.

Timer engine, exact-second checkpoints, mode detection, saved data compatibility and watcher handoff are covered by the existing feature and lifecycle suites. This suite adds Home, range-linking, overlay, pending-default, alert keyboard and layout integration coverage.

Preview artifacts: `test-output/integration-home-100.png`, `test-output/integration-home-125.png`, `test-output/integration-home-150.png`, and matching `integration-reports-100.png`, `integration-reports-125.png`, `integration-reports-150.png` in `test-output`. Each scale also has `-lower` and `-history` Home previews and a `-history` Reports preview showing content reached by scrolling. They contain test fixtures only.
