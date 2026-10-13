import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dailySeconds, parseHistory, secondsInRange } from "../src/history.ts";
import { selectSessionState, startOfReportingWeek } from "../src/session.ts";
import { advanceServerClock, countdownTick, secondsUntil } from "../src/timerClock.ts";

const boundaryHistory = `------------------------------------------------------------
SessionId: boundary
Mode: work
Applications: editor
Started: 2026-10-10 23:50:00
Finished: 2026-10-11 00:10:00
ActiveSeconds: 1200
ActiveSegments: [{"StartedAt":"2026-10-10T23:50:00+08:00","EndedAt":"2026-10-11T00:10:00+08:00"}]
Total work time: 20 min (0.333 hours)
Timer cycles: 1
------------------------------------------------------------`;
const boundary = parseHistory(boundaryHistory)[0];
assert.equal(secondsInRange(boundary, new Date("2026-10-11T00:00:00+08:00"), new Date("2026-10-11T23:59:59+08:00")), 600, "midnight range must include only the overlapping active segment");
assert.deepEqual(Array.from(dailySeconds(boundary).values()).map(Math.round), [600, 600], "a cross-midnight segment must split across local days");

const pausedHistory = `------------------------------------------------------------
Mode: game
Applications: game
Started: 2026-10-10 10:00:00
Finished: 2026-10-10 11:00:00
ActiveSeconds: 900
ActiveSegments: [{"StartedAt":"2026-10-10T10:00:00+08:00","EndedAt":"2026-10-10T10:10:00+08:00"},{"StartedAt":"2026-10-10T10:45:00+08:00","EndedAt":"2026-10-10T10:50:00+08:00"}]
Total game time: 15 min (0.25 hours)
Timer cycles: 0
------------------------------------------------------------`;
assert.equal(secondsInRange(parseHistory(pausedHistory)[0]), 900, "paused gaps must not count as active time");

const legacyHistory = `------------------------------------------------------------
Game: legacy
Started: 2026-10-10 10:00:00
Finished: 2026-10-10 11:22:00
Total game time: 1 hr 22 min (1.367 hours)
Timer cycles: 2
------------------------------------------------------------`;
assert.equal(parseHistory(legacyHistory)[0].seconds, 4920, "legacy hour and minute tokens must both be counted");
assert.equal(startOfReportingWeek(new Date("2026-10-11T12:00:00+08:00")).toISOString(), new Date("2026-10-05T00:00:00+08:00").toISOString(), "weekly reports must begin on Monday");
assert.equal(secondsInRange(boundary, new Date("2026-10-01T00:00:00+08:00"), new Date("2026-10-11T00:00:00+08:00")), 600, "month and range boundaries must clip active segments exactly");
const staleRuntime = { Mode: "work", SessionId: "same-session", SessionElapsedSeconds: 999, SessionRunningSince: "2026-10-10T10:00:00+08:00" };
const committedPause = { Mode: "work", SessionId: "same-session", ElapsedSeconds: 202, RunningSince: null, Paused: true };
assert.equal(selectSessionState(staleRuntime, committedPause, "work"), committedPause, "a committed paused checkpoint must beat stale runtime for the same session");
assert.equal(selectSessionState({ ...staleRuntime, SessionId: "new-session" }, committedPause, "work")?.SessionId, "new-session", "a paused checkpoint must not replace a different runtime session");
const timerServerUtc = "2026-10-12T10:00:00.000Z";
const timerDeadline = "2026-10-12T10:00:10.000Z";
const timerReceivedAt = 5000;
assert.deepEqual([0, 1000, 2000].map(elapsed => Math.ceil(secondsUntil(timerDeadline, advanceServerClock(timerServerUtc, timerReceivedAt, timerReceivedAt + elapsed)))), [10, 9, 8], "the display timer must advance one second at a time between two-second backend snapshots");
assert.deepEqual([0, 1000, 2000].map(elapsed => countdownTick(timerDeadline, advanceServerClock(timerServerUtc, timerReceivedAt, timerReceivedAt + elapsed))), [10, 9, 8], "animation-frame rendering must expose every countdown second");
const dashboardSource = readFileSync(new URL("../src/components/Dashboard.tsx", import.meta.url), "utf8");
const appSource = readFileSync(new URL("../src/App.tsx", import.meta.url), "utf8");
assert.match(dashboardSource, /requestAnimationFrame\(update\)/, "the dashboard must render countdown boundaries from an animation-frame clock");
assert.match(dashboardSource, /countdownTick\(timer\?\.Deadline, next\)/, "the dashboard must update only when the visible countdown second changes");
assert.match(appSource, /startSerializedPolling\(/, "snapshot polling must use the serialized polling controller");
assert.doesNotMatch(appSource, /setInterval\(refresh/, "snapshot polling must not overlap through setInterval");

const overlaysSource = readFileSync(new URL("../src/components/Overlays.tsx", import.meta.url), "utf8");
assert.match(overlaysSource, /toggleFromTrigger/, "rail clicks must have an explicit dock-toggle path");
assert.match(overlaysSource, /hasDockedPanel \? "w-\[332px\]" : "w-11"/, "a clicked or pinned panel must reserve a column beside the main page");
assert.match(overlaysSource, /hasDockedPanel \? "relative my-3 ml-2/, "docked panels must participate in workspace layout instead of covering the main page");
assert.match(overlaysSource, /: "absolute left-\[52px\] top-3/, "hover-only panels must retain a lightweight floating preview");
console.log("React history and display-clock tests passed.");
