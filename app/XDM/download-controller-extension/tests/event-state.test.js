import assert from "node:assert/strict";
import test from "node:test";
import { reconcileEvent } from "../event-state.js";
import { transitionActivity, summarizeDownloads, DEFAULT_ACTIVITY_STATE } from "../controller-state.js";

const dateAdded = "2026-10-03T10:00:01Z";
const settings = () => ({
  browserSessionStartedAt: Date.parse("2026-10-03T10:00:00Z"),
  controllerDownloads: [], controllerFinishedIds: [],
  controllerEventCursor: { epoch: "backend", sequence: 0 }
});
function event(sequence, state, kind = "started") {
  return { type: "downloads", epoch: "backend", sequence, kind, id: "tiny", delta: true,
    downloads: [{ id: "tiny", state, dateAdded }] };
}
function commit(stored, reduced) {
  stored.controllerDownloads = reduced.downloads;
  stored.controllerFinishedIds = reduced.finishedIds;
  stored.controllerEventCursor = reduced.cursor;
}
test("start and finish arriving immediately are both processed and counted", () => {
  const stored = settings();
  const started = reconcileEvent(event(1, "Downloading"), stored);
  let activity = transitionActivity(summarizeDownloads(started.downloads), DEFAULT_ACTIVITY_STATE);
  assert.equal(activity.toolbar.badgeText, "1");
  commit(stored, started);
  const finished = reconcileEvent(event(2, "Finished", "finished"), stored);
  activity = transitionActivity(summarizeDownloads(finished.downloads), activity.state, finished.completed ? "download-finished" : "backend");
  assert.equal(activity.toolbar.badgeText, "✓");
  commit(stored, finished);
  assert.equal(reconcileEvent(event(2, "Finished", "finished"), stored), null);
});
test("completion does not require ever observing an active state", () => {
  const reduced = reconcileEvent(event(1, "Finished", "finished"), settings());
  assert.equal(transitionActivity(summarizeDownloads(reduced.downloads), DEFAULT_ACTIVITY_STATE, "download-finished").toolbar.badgeText, "✓");
});
test("resync after a journal gap discovers a missed completion", () => {
  const stored = settings();
  const snapshot = { ...event(600, "Finished", "snapshot"), delta: false };
  const reduced = reconcileEvent(snapshot, stored);
  assert.equal(reduced.completed, true);
  commit(stored, reduced);
  assert.equal(reconcileEvent(snapshot, stored).completed, false);
});
test("backend restart accepts the new epoch and lower sequence", () => {
  const stored = settings();
  stored.controllerEventCursor.sequence = 500;
  const message = { ...event(0, "Finished", "snapshot"), epoch: "restarted", delta: false };
  assert.equal(reconcileEvent(message, stored).cursor.epoch, "restarted");
});
test("completion still appears when another download is stopped", () => {
  const stored = settings();
  stored.controllerDownloads = [{ id: "paused", state: "Stopped", dateAdded }];
  const reduced = reconcileEvent(event(1, "Finished", "finished"), stored);
  assert.equal(reduced.downloads.length, 2);
  assert.equal(transitionActivity(summarizeDownloads(reduced.downloads), DEFAULT_ACTIVITY_STATE, "download-finished").toolbar.badgeText, "✓");
});
test("history and out-of-scope completions do not light the session badge", () => {
  const stored = settings();
  const old = { ...event(1, "Finished", "finished"), downloads: [{ id: "tiny", state: "Finished", dateAdded: "2025-01-01T00:00:00Z" }] };
  assert.equal(reconcileEvent(old, stored).completed, false);
  stored.showAllDownloads = true;
  assert.equal(reconcileEvent({ ...old, kind: "snapshot", delta: false }, stored).completed, false);
});
test("dismissing the badge or changing scope does not rediscover completion", () => {
  const stored = settings();
  commit(stored, reconcileEvent(event(1, "Finished", "finished"), stored));
  assert.equal(reconcileEvent(null, stored, "controller-opened").completed, false);
  assert.equal(reconcileEvent(null, stored, "scope-changed").completed, false);
});
test("delta deletion keeps other downloads and snapshot removes stale entries", () => {
  const stored = settings();
  stored.controllerDownloads = [{ id: "tiny", state: "Stopped", dateAdded }, { id: "other", state: "Downloading", dateAdded }];
  const removed = reconcileEvent({ ...event(1, "Stopped", "removed"), downloads: [] }, stored);
  assert.deepEqual(removed.downloads.map(item => item.id), ["other"]);
  commit(stored, removed);
  const snapshot = reconcileEvent({ ...event(2, "Stopped", "snapshot"), delta: false, downloads: [] }, stored);
  assert.equal(snapshot.downloads.length, 0);
});
