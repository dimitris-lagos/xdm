import { filterDownloads } from "./download-scope.js";

// Cursor and activity are committed together only after processing each event.
// Snapshots detect completions even after the backend journal has rolled over.
export function reconcileEvent(message, settings, uiEvent = "backend") {
  const previousCursor = settings.controllerEventCursor || {};
  if (message && (!Array.isArray(message.downloads) || !message.epoch ||
    !Number.isSafeInteger(message.sequence) || message.sequence < 0)) return null;
  if (message && message.kind !== "snapshot" && message.epoch === previousCursor.epoch &&
    message.sequence <= previousCursor.sequence) return null;
  let downloads = message?.downloads || settings.controllerDownloads || [];
  if (message?.delta) {
    const entries = new Map((settings.controllerDownloads || []).map(item => [item.id, item]));
    if (message.kind === "removed") entries.delete(message.id);
    else for (const item of message.downloads) entries.set(item.id, item);
    downloads = [...entries.values()].sort((a, b) => Date.parse(b.dateAdded) - Date.parse(a.dateAdded));
  }
  const visible = filterDownloads(downloads, settings.showAllDownloads === true,
    settings.browserSessionStartedAt || Date.now());
  const finishedIds = visible.filter(item => item.state === "Finished").map(item => item.id);
  const known = new Set(settings.controllerFinishedIds || []);
  const sessionStartedAt = settings.browserSessionStartedAt || Date.now();
  const completed = uiEvent === "backend" && (
    (message?.kind === "finished" && visible.some(item => item.id === message.id)) ||
    visible.some(item => item.state === "Finished" && !known.has(item.id) &&
      Date.parse(item.dateAdded) >= sessionStartedAt)
  );
  return {
    downloads, finishedIds, completed,
    cursor: message ? { epoch: message.epoch, sequence: message.sequence } : previousCursor
  };
}
