import { getDownloads } from "./api.js";
import {
  DEFAULT_ACTIVITY_STATE,
  summarizeDownloads,
  transitionActivity
} from "./controller-state.js";
import { filterDownloads } from "./download-scope.js";

const ALARM_NAME = "xdm-controller-poll";
const ACTIVITY_STATE_KEY = "controllerActivityState";
const FAST_POLL_MS = 2000;
let pollInFlight = null;

async function setToolbar({ mode, badgeText = "", pulseOn = false }) {
  const colors = {
    active: pulseOn ? "#7c3aed" : "#24a861",
    finished: "#397ec1",
    idle: "#397ec1",
    offline: "#d14d5a"
  };
  const titles = {
    active: `${badgeText} active XDM download${badgeText === "1" ? "" : "s"}`,
    finished: "XDM downloads finished — open to dismiss",
    idle: "XDM Download Controller",
    offline: "XDM is offline"
  };
  await Promise.all([
    chrome.action.setBadgeText({ text: badgeText }),
    chrome.action.setBadgeBackgroundColor({ color: colors[mode] }),
    chrome.action.setTitle({ title: titles[mode] })
  ]);
}

async function observeDownloads(event = "poll") {
  const [downloads, settings] = await Promise.all([
    getDownloads(),
    chrome.storage.local.get([
      "showAllDownloads",
      "browserSessionStartedAt",
      ACTIVITY_STATE_KEY
    ])
  ]);
  const visible = filterDownloads(
    downloads,
    settings.showAllDownloads === true,
    settings.browserSessionStartedAt || Date.now()
  );
  const transition = transitionActivity(
    summarizeDownloads(visible),
    settings[ACTIVITY_STATE_KEY] || DEFAULT_ACTIVITY_STATE,
    event
  );
  await chrome.storage.local.set({ [ACTIVITY_STATE_KEY]: transition.state });
  await setToolbar(transition.toolbar);
}

async function poll(event = "poll") {
  if (pollInFlight) return pollInFlight;
  pollInFlight = observeDownloads(event)
    .catch(() => setToolbar({ mode: "offline", badgeText: "!" }))
    .finally(() => { pollInFlight = null; });
  return pollInFlight;
}

function ensureSessionStart(reset = false) {
  return chrome.storage.local.get("browserSessionStartedAt").then(value => {
    if (!reset && Number.isFinite(value.browserSessionStartedAt)) return;
    return chrome.storage.local.set({ browserSessionStartedAt: Date.now() });
  });
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
  ensureSessionStart().then(() => poll());
});

chrome.runtime.onStartup.addListener(() => {
  ensureSessionStart(true).then(() => poll());
});

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === ALARM_NAME) poll();
});

chrome.runtime.onMessage.addListener(message => {
  if (message?.type === "controller-opened") poll("controller-opened");
  else if (message?.type === "downloads-changed" || message?.type === "scope-changed") poll();
});

chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
ensureSessionStart().then(() => poll());
setInterval(() => poll(), FAST_POLL_MS);
