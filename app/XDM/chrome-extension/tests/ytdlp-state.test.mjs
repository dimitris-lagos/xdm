import test from "node:test";
import assert from "node:assert/strict";
import { ExtensionStateStore } from "../extension-state.mjs";
test("persisted app preference disables extraction independently of browser monitoring", () => {
 const store = new ExtensionStateStore();
 store.applySync({ enabled: true, ytdlpEnabled: false });
 assert.equal(store.snapshot().ytdlpEnabled, false);
 assert.equal(store.snapshot().monitoringEnabled, true);
 const revision = store.snapshot().revision;
 store.applySync({ enabled: true, ytdlpEnabled: false });
 assert.equal(store.snapshot().revision, revision);
 store.applySync({ enabled: true, ytdlpEnabled: true });
 assert.equal(store.snapshot().ytdlpEnabled, true);
 assert.equal(store.snapshot().mediaRevision, 0);
});
