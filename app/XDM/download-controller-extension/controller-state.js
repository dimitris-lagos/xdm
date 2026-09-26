export function summarizeDownloads(downloads) {
  const activeCount = downloads.filter(item => item.state === "Downloading" || item.state === "Waiting").length;
  return {
    activeCount,
    isActive: activeCount > 0,
    allFinished: downloads.length > 0 && downloads.every(item => item.state === "Finished")
  };
}

export const DEFAULT_ACTIVITY_STATE = Object.freeze({
  hadActiveDownloads: false,
  completionPending: false,
  pulseOn: false
});

export function normalizeActivityState(state = {}) {
  return {
    hadActiveDownloads: state.hadActiveDownloads === true,
    completionPending: state.completionPending === true,
    pulseOn: state.pulseOn === true
  };
}

export function transitionActivity(summary, previousState, event = "poll") {
  const state = normalizeActivityState(previousState);
  if (event === "controller-opened") state.completionPending = false;

  if (summary.isActive) {
    state.hadActiveDownloads = true;
    state.completionPending = false;
    state.pulseOn = !state.pulseOn;
    return { state, toolbar: { mode: "active", badgeText: String(summary.activeCount), pulseOn: state.pulseOn } };
  }

  if (state.hadActiveDownloads && summary.allFinished) {
    state.hadActiveDownloads = false;
    state.completionPending = true;
  }

  if (state.completionPending) {
    return { state, toolbar: { mode: "finished", badgeText: "✓", pulseOn: false } };
  }

  return { state, toolbar: { mode: "idle", badgeText: "", pulseOn: false } };
}

export function actionsForState(state) {
  switch (state) {
    case "Downloading": return ["pause", "stop"];
    case "Waiting": return ["stop"];
    case "Stopped": return ["resume", "restart"];
    case "Finished": return ["open", "open-folder"];
    default: return [];
  }
}
