export function filterDownloads(downloads, showAllDownloads, sessionStartedAt) {
  if (showAllDownloads) return downloads;
  return downloads.filter(download => {
    const addedAt = Date.parse(download.dateAdded);
    return Number.isFinite(addedAt) && addedAt >= sessionStartedAt;
  });
}
