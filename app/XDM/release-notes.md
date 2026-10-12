# XDM 9.0.3 Beta

The Integration Module now shows media from the active browser tab only.

- Scope the media list, codec/quality filter choices, and toolbar count to the active tab.
- Refresh the list when switching tabs or focused browser windows, without waiting for the next backend sync.
- Resolve the active tab when the extension worker starts and when the popup opens.
- Ignore stale asynchronous tab-query results and hide media without a known tab association.
- Keep captures from other tabs available when returning to those tabs.

Includes Integration Module **3.6.5** and the existing Live Download Controller. The installer supports both x86 and x64 Windows. Media versions remain yt-dlp **2026.08.19** and FFmpeg **N-127043-g5a54fcf75e-20260930**.

## Install or upgrade

Download **XDM-Setup.exe** below. Setup requires administrator rights and .NET Framework 4.7.2+. Internet access is needed for selected media-tool downloads.

After upgrading, click **Reload** on the Integration Module in the browser's extensions page. Stable extension folders and personal download data are preserved.

See the [README](https://github.com/dimitris-lagos/xdm/blob/v9.0.3/README.md) for setup instructions.

## Validation

Both Windows architectures, native SQLite read/write, media-tool verification, backend tests, and browser-extension tests are checked by the release build before publishing.
