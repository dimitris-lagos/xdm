import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { actionsForState, summarizeDownloads } from "../controller-state.js";
import { filterDownloads } from "../download-scope.js";

test("maps states to the intended actions", () => {
  assert.deepEqual(actionsForState("Downloading"), ["pause", "stop"]);
  assert.deepEqual(actionsForState("Waiting"), ["stop"]);
  assert.deepEqual(actionsForState("Stopped"), ["resume", "restart"]);
  assert.deepEqual(actionsForState("Finished"), ["restart"]);
});

test("aggregates active, finished, stopped, and empty lists", () => {
  assert.deepEqual(summarizeDownloads([{ state: "Downloading" }, { state: "Waiting" }]), {
    activeCount: 2, isActive: true, allFinished: false
  });
  assert.equal(summarizeDownloads([{ state: "Finished" }]).allFinished, true);
  assert.equal(summarizeDownloads([{ state: "Finished" }, { state: "Stopped" }]).allFinished, false);
  assert.equal(summarizeDownloads([]).allFinished, false);
});

test("manifest uses only narrow permissions", async () => {
  const manifest = JSON.parse(await readFile(new URL("../manifest.json", import.meta.url), "utf8"));
  assert.deepEqual(manifest.permissions, ["alarms", "storage"]);
  assert.deepEqual(manifest.host_permissions, ["http://127.0.0.1:8597/*"]);
  const hash = createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest().subarray(0, 16);
  const extensionId = [...hash].map(byte => String.fromCharCode(97 + (byte >> 4), 97 + (byte & 15))).join("");
  assert.equal(extensionId, "hmbfgklncdkaclckkibeflpmlaedhgck");
  assert.deepEqual(manifest.action.default_icon, {
    "16": "icon16.png", "48": "icon48.png", "128": "icon128.png"
  });
});

test("controller toolbar icons are distinct from the integration module", async () => {
  for (const size of [16, 48, 128]) {
    const controllerIcon = await readFile(new URL(`../icon${size}.png`, import.meta.url));
    const integrationIcon = await readFile(new URL(`../../chrome-extension/icon${size}.png`, import.meta.url));
    assert.notEqual(createHash("sha256").update(controllerIcon).digest("hex"),
      createHash("sha256").update(integrationIcon).digest("hex"));
  }
});

test("session scope keeps only downloads added since Opera started", () => {
  const sessionStartedAt = Date.parse("2026-09-26T10:00:00");
  const downloads = [
    { id: "old", dateAdded: "2026-09-26T09:59:59" },
    { id: "new", dateAdded: "2026-09-26T10:00:00" },
    { id: "newer", dateAdded: "2026-09-26T10:05:00" },
    { id: "missing" },
    { id: "invalid", dateAdded: "not-a-date" }
  ];

  assert.deepEqual(filterDownloads(downloads, false, sessionStartedAt).map(item => item.id), ["new", "newer"]);
  assert.equal(filterDownloads(downloads, true, sessionStartedAt), downloads);
});

test("API identifies the fixed extension on every request", async () => {
  const api = await readFile(new URL("../api.js", import.meta.url), "utf8");
  assert.equal(api.includes('const CLIENT_HEADER = "X-XDM-Controller-Client"'), true);
  assert.equal(api.includes('const CLIENT_ID = "hmbfgklncdkaclckkibeflpmlaedhgck"'), true);
  assert.equal(api.includes("[CLIENT_HEADER]: CLIENT_ID"), true);
});

test("popup does not inject server data with innerHTML", async () => {
  const popup = await readFile(new URL("../popup.js", import.meta.url), "utf8");
  assert.equal(popup.includes("innerHTML"), false);
  assert.equal(popup.includes("textContent"), true);
});

test("popup layout is compact and prevents horizontal overflow", async () => {
  const css = await readFile(new URL("../popup.css", import.meta.url), "utf8");
  assert.equal(css.includes("width: 340px"), true);
  assert.equal(css.includes("min-width: 340px"), true);
  assert.equal(css.includes("100vw"), false);
  assert.equal(css.includes("overflow-x: hidden"), true);
  assert.equal(css.includes("grid-template-columns: minmax(0, 1fr) auto"), true);
});
