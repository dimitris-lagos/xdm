import { connectEvents } from "./api.js";
import { DEFAULT_ACTIVITY_STATE, summarizeDownloads, transitionActivity } from "./controller-state.js";
import { filterDownloads } from "./download-scope.js";
import { reconcileEvent } from "./event-state.js";

const ALARM_NAME = "xdm-controller-reconnect";
const ACTIVITY_STATE_KEY = "controllerActivityState";
let socket = null;
let connecting = null;
let queue = Promise.resolve();
let retryTimer = null;
let watchdog = null;
let sessionGeneration = 0;

async function setToolbar({ mode, badgeText = "", pulseOn = false }) {
  const colors = {
    active: pulseOn ? "#7c3aed" : "#24a861",
    finished: "#397ec1", idle: "#397ec1", offline: "#d14d5a"
  };
  const titles = {
    active: badgeText + " active XDM downloads",
    finished: "XDM downloads finished — open to dismiss",
    idle: "XDM Download Controller", offline: "XDM is offline"
  };
  await Promise.all([
    chrome.action.setBadgeText({ text: badgeText }),
    chrome.action.setBadgeBackgroundColor({ color: colors[mode] }),
    chrome.action.setTitle({ title: titles[mode] })
  ]);
}

function enqueue(work) {
  queue = queue.then(work).catch(error => {
    console.error("XDM controller event:", error);
    socket?.close();
  });
  return queue;
}

async function applyEvent(message, uiEvent = "backend") {
  const settings = await chrome.storage.local.get([
    "showAllDownloads", "browserSessionStartedAt", ACTIVITY_STATE_KEY,
    "controllerEventCursor", "controllerDownloads", "controllerFinishedIds"
  ]);
  const reduced = reconcileEvent(message, settings, uiEvent);
  if (!reduced) return;
  const visible = filterDownloads(reduced.downloads, settings.showAllDownloads === true,
    settings.browserSessionStartedAt || Date.now());
  const transition = transitionActivity(summarizeDownloads(visible),
    settings[ACTIVITY_STATE_KEY] || DEFAULT_ACTIVITY_STATE,
    reduced.completed ? "download-finished" : uiEvent);
  await chrome.storage.local.set({
    controllerDownloads: reduced.downloads,
    controllerEventCursor: reduced.cursor,
    controllerFinishedIds: reduced.finishedIds,
    [ACTIVITY_STATE_KEY]: transition.state
  });
  await setToolbar(transition.toolbar);
  chrome.runtime.sendMessage({ type: "backend-downloads", downloads: reduced.downloads }).catch(() => {});
}

async function ensureSessionStart(reset = false) {
  const value = await chrome.storage.local.get("browserSessionStartedAt");
  if (!reset && Number.isFinite(value.browserSessionStartedAt)) return;
  await chrome.storage.local.set({
    browserSessionStartedAt: Date.now(),
    controllerFinishedIds: [], controllerEventCursor: {},
    controllerDownloads: [], [ACTIVITY_STATE_KEY]: DEFAULT_ACTIVITY_STATE
  });
}

function armWatchdog(current) {
  clearTimeout(watchdog);
  watchdog = setTimeout(() => current.close(), 45000);
}

function reconnectLater() {
  clearTimeout(retryTimer);
  retryTimer = setTimeout(ensureConnection, 2000);
}

function ensureConnection() {
  if (connecting || (socket && socket.readyState < WebSocket.CLOSING)) return connecting;
  const generation = sessionGeneration;
  connecting = (async () => {
    await ensureSessionStart();
    const settings = await chrome.storage.local.get("controllerEventCursor");
    const current = await connectEvents(settings.controllerEventCursor);
    if (generation !== sessionGeneration) { current.close(); reconnectLater(); return; }
    socket = current;
    current.onopen = () => {
      armWatchdog(current);
      enqueue(() => applyEvent(null, "reconnected"));
    };
    current.onmessage = ({ data }) => {
      armWatchdog(current);
      try {
        const message = JSON.parse(data);
        if (message.type === "downloads") enqueue(() => applyEvent(message));
      } catch { current.close(); }
    };
    current.onerror = () => current.close();
    current.onclose = () => {
      if (socket !== current) return;
      socket = null;
      clearTimeout(watchdog);
      enqueue(() => setToolbar({ mode: "offline", badgeText: "!" }));
      chrome.runtime.sendMessage({ type: "backend-offline" }).catch(() => {});
      reconnectLater();
    };
  })().catch(() => {
    enqueue(() => setToolbar({ mode: "offline", badgeText: "!" }));
    reconnectLater();
  }).finally(() => { connecting = null; });
  return connecting;
}

chrome.runtime.onInstalled.addListener(() => ensureConnection());
chrome.runtime.onStartup.addListener(() => {
  sessionGeneration++;
  socket?.close();
  enqueue(() => ensureSessionStart(true)).then(ensureConnection);
});
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === ALARM_NAME) ensureConnection();
});
chrome.runtime.onMessage.addListener(message => {
  if (message?.type === "controller-opened" || message?.type === "scope-changed") {
    enqueue(() => applyEvent(null, message.type));
  }
  if (message?.type !== "backend-downloads" && message?.type !== "backend-offline") ensureConnection();
});
chrome.alarms.clear("xdm-controller-poll");
chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
ensureConnection();
