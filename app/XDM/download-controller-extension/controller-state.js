export function summarizeDownloads(downloads) {
  const activeCount = downloads.filter(item => item.state === "Downloading" || item.state === "Waiting").length;
  return {
    activeCount,
    isActive: activeCount > 0,
    allFinished: downloads.length > 0 && downloads.every(item => item.state === "Finished")
  };
}

export function actionsForState(state) {
  switch (state) {
    case "Downloading": return ["pause", "stop"];
    case "Waiting": return ["stop"];
    case "Stopped": return ["resume", "restart"];
    case "Finished": return ["restart"];
    default: return [];
  }
}
