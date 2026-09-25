import { getDownloads } from "./api.js";
import { summarizeDownloads } from "./controller-state.js";
import { filterDownloads } from "./download-scope.js";

const ALARM_NAME = "xdm-controller-poll";

async function setToolbar(mode, badgeText = "") {
  await chrome.action.setBadgeText({ text: badgeText });
  await chrome.action.setBadgeBackgroundColor({ color: mode === "active" ? "#20a85a" : "#397ec1" });
  await chrome.action.setTitle({ title: mode === "offline" ? "XDM is offline" : "XDM Download Controller" });
}

async function poll() {
  try {
    const [downloads, settings] = await Promise.all([
      getDownloads(),
      chrome.storage.local.get(["showAllDownloads", "browserSessionStartedAt"])
    ]);
    const visible = filterDownloads(downloads, settings.showAllDownloads === true,
      settings.browserSessionStartedAt || Date.now());
    const summary = summarizeDownloads(visible);
    if (summary.isActive) await setToolbar("active", String(summary.activeCount));
    else if (summary.allFinished) await setToolbar("finished", "✓");
    else await setToolbar("idle", "");
  } catch (_) {
    await setToolbar("offline", "!");
  }
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
  chrome.storage.local.get("browserSessionStartedAt").then(value => {
    if (!Number.isFinite(value.browserSessionStartedAt)) {
      return chrome.storage.local.set({ browserSessionStartedAt: Date.now() });
    }
  });
  poll();
});
chrome.runtime.onStartup.addListener(() => {
  chrome.storage.local.set({ browserSessionStartedAt: Date.now() }).then(poll);
});
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === ALARM_NAME) poll();
});
chrome.runtime.onMessage.addListener(message => {
  if (message?.type === "downloads-changed" || message?.type === "scope-changed") poll();
});

chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
chrome.storage.local.get("browserSessionStartedAt").then(value => {
  if (Number.isFinite(value.browserSessionStartedAt)) return;
  return chrome.storage.local.set({ browserSessionStartedAt: Date.now() });
}).then(poll);
