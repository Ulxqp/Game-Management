import assert from "node:assert/strict";
import { startSerializedPolling } from "../src/bridge.ts";

const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const abortError = () => Object.assign(new Error("aborted"), { name: "AbortError" });

{
  let calls = 0;
  let inFlight = 0;
  let maximumInFlight = 0;
  const values = [];
  const pending = [];
  const poller = startSerializedPolling({
    request: signal => new Promise((resolve, reject) => {
      const call = ++calls;
      inFlight++;
      maximumInFlight = Math.max(maximumInFlight, inFlight);
      pending.push({ resolve: () => { inFlight--; resolve(call); }, reject });
      signal.addEventListener("abort", () => { inFlight--; reject(abortError()); }, { once: true });
    }),
    onValue: value => values.push(value),
    onError: error => { throw error; },
    intervalMs: 10000,
    timeoutMs: 1000,
    delayedAfterMs: 50
  });
  void poller.requestNow();
  void poller.requestNow();
  assert.equal(calls, 1, "manual refreshes must queue behind the active request");
  pending.shift().resolve();
  await Promise.resolve(); await Promise.resolve();
  assert.equal(calls, 2, "one queued refresh must begin after the active request settles");
  pending.shift().resolve();
  await Promise.resolve(); await Promise.resolve();
  poller.stop();
  assert.equal(maximumInFlight, 1, "automatic and manual refreshes must share one in-flight request");
  assert.equal(values.length, 2, "a queued refresh must run after the active request finishes");
  assert.deepEqual(values, [...values].sort((left, right) => left - right), "older responses must never overwrite newer snapshots");
}

{
  let calls = 0;
  let aborted = 0;
  const values = [];
  const errors = [];
  const poller = startSerializedPolling({
    request: signal => {
      const call = ++calls;
      if (call > 1) return Promise.resolve(call);
      return new Promise((_, reject) => signal.addEventListener("abort", () => { aborted++; reject(abortError()); }, { once: true }));
    },
    onValue: value => values.push(value),
    onError: (error, timedOut) => errors.push({ error, timedOut }),
    intervalMs: 1,
    timeoutMs: 12,
    delayedAfterMs: 4
  });
  await wait(45);
  poller.stop();
  assert.equal(aborted, 1, "a hung request must be aborted at the timeout");
  assert.equal(errors.length, 1, "the timeout must be reported once");
  assert.equal(errors[0].timedOut, true, "timeout failures must be distinguishable from ordinary bridge errors");
  assert.ok(values.some(value => value > 1), "polling must recover after a timed-out request");
}

{
  let resolveRequest;
  let aborted = 0;
  const values = [];
  const poller = startSerializedPolling({
    request: signal => new Promise(resolve => {
      resolveRequest = resolve;
      signal.addEventListener("abort", () => { aborted++; }, { once: true });
    }),
    onValue: value => values.push(value),
    onError: () => {},
    intervalMs: 1,
    timeoutMs: 100,
    delayedAfterMs: 50
  });
  await wait(2);
  poller.stop();
  resolveRequest?.(99);
  await wait(5);
  assert.equal(aborted, 1, "stopping the poller must abort the active request");
  assert.deepEqual(values, [], "a response settling after unmount must not update application state");
}

console.log("Serialized snapshot polling tests passed.");
