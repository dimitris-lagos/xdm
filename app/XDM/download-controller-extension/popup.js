import { getDownloads, runAction } from "./api.js";
import { filterDownloads } from "./download-scope.js";

const list = document.querySelector("#downloads");
const empty = document.querySelector("#empty");
const status = document.querySelector("#connection-status");
const template = document.querySelector("#download-template");
const settingsPanel = document.querySelector("#settings-panel");
const showAllInput = document.querySelector("#show-all");
const scopeDescription = document.querySelector("#scope-description");
const scopeLabel = document.querySelector("#scope-label");
let allDownloads = [];
let sessionStartedAt = Date.now();
let showAllDownloads = false;

chrome.runtime.sendMessage({ type: "controller-opened" });

function formatBytes(value) {
  if (!Number.isFinite(value) || value < 0) return "Size unknown";
  const units = ["B", "KiB", "MiB", "GiB", "TiB"];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit += 1; }
  return `${size >= 10 || unit === 0 ? size.toFixed(0) : size.toFixed(1)} ${units[unit]}`;
}

function render(downloads) {
  list.replaceChildren();
  empty.textContent = showAllDownloads ? "No downloads yet." : "No downloads in this Opera session.";
  empty.hidden = downloads.length !== 0;
  for (const download of downloads) {
    const card = template.content.firstElementChild.cloneNode(true);
    card.querySelector(".name").textContent = download.name || "Unnamed download";
    const state = card.querySelector(".state");
    state.textContent = download.state;
    state.dataset.state = download.state;
    card.querySelector(".progress-bar").style.width = `${Math.max(0, Math.min(100, download.progress || 0))}%`;
    const sizeText = download.totalBytes == null
      ? "Size unknown"
      : `${formatBytes(download.downloadedBytes || 0)} / ${formatBytes(download.totalBytes)}`;
    card.querySelector(".size").textContent = sizeText;
    card.querySelector(".speed").textContent = download.speed || "";
    card.querySelector(".eta").textContent = download.eta ? `ETA ${download.eta}` : "";
    const actions = card.querySelector(".actions");
    for (const action of download.actions || []) {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = action === "open-folder"
        ? "Open folder"
        : action.charAt(0).toUpperCase() + action.slice(1);
      button.addEventListener("click", async () => {
        for (const item of actions.querySelectorAll("button")) item.disabled = true;
        try {
          await runAction(download.id, action);
          chrome.runtime.sendMessage({ type: "downloads-changed" });
          await refresh();
        } catch (error) {
          setConnection(false, error.message);
          for (const item of actions.querySelectorAll("button")) item.disabled = false;
        }
      });
      actions.append(button);
    }
    list.append(card);
  }
}

function updateScopeUi() {
  showAllInput.checked = showAllDownloads;
  scopeDescription.textContent = showAllDownloads ? "Complete XDM history" : "This Opera session";
  scopeLabel.textContent = showAllDownloads ? "All" : "Session";
}

function setConnection(online, message = "") {
  status.className = online ? "online" : "offline";
  status.textContent = online ? (message || "Connected") : (message || "XDM is offline");
}

async function refresh() {
  try {
    allDownloads = await getDownloads();
    const downloads = filterDownloads(allDownloads, showAllDownloads, sessionStartedAt);
    render(downloads);
    const scope = showAllDownloads ? "all" : "session";
    setConnection(true, `${downloads.length} ${scope}`);
  } catch (error) {
    list.replaceChildren();
    empty.hidden = false;
    empty.textContent = "Start XDM to view your downloads.";
    setConnection(false, error?.message || "XDM is offline");
  }
}

async function loadSettings() {
  const stored = await chrome.storage.local.get(["showAllDownloads", "browserSessionStartedAt"]);
  showAllDownloads = stored.showAllDownloads === true;
  if (Number.isFinite(stored.browserSessionStartedAt)) sessionStartedAt = stored.browserSessionStartedAt;
  else await chrome.storage.local.set({ browserSessionStartedAt: sessionStartedAt });
  updateScopeUi();
}

document.querySelector("#settings").addEventListener("click", () => {
  settingsPanel.hidden = !settingsPanel.hidden;
});
showAllInput.addEventListener("change", async () => {
  showAllDownloads = showAllInput.checked;
  updateScopeUi();
  await chrome.storage.local.set({ showAllDownloads });
  const downloads = filterDownloads(allDownloads, showAllDownloads, sessionStartedAt);
  render(downloads);
  setConnection(true, `${downloads.length} ${showAllDownloads ? "all" : "session"}`);
  chrome.runtime.sendMessage({ type: "scope-changed" });
});
document.querySelector("#refresh").addEventListener("click", refresh);
loadSettings().then(refresh);
setInterval(refresh, 3000);
