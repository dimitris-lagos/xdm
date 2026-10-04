# XDM 9.0.2 Beta

This Windows beta adds live browser download control, richer media discovery, and a new installer for both 32-bit and 64-bit Windows.

## Live Download Controller

- Receive authenticated WebSocket events directly from the backend, replacing periodic download polling.
- Count active and queued downloads on the toolbar, including downloads that start and finish immediately.
- Show live progress, transferred size, speed, and ETA.
- Pause, stop, resume, restart, and open completed downloads or their folders.
- Choose the current browser session or complete history.
- Replay recent events on reconnect, reject duplicate events, and synchronize with a fresh snapshot after a backend restart or an expired cursor.

The controller requires Chromium 116 or newer. The backend keeps the last 512 events; snapshots recover entries still present in XDM.

## Improved Integration Module

- Filter detected media by extension, video/audio codec family, video/sound quality, and minimum size.
- Select several formats with **Download checked**.
- Discover YouTube formats as tabs navigate, using structured metadata.
- Control automatic yt-dlp extraction independently from regular browser monitoring.
- Cancel running extraction processes when yt-dlp extraction is switched off.
- Prevent duplicate downloads from repeated browser filename callbacks.

## Desktop cleanup

**Clean Completed** clears completed list entries while keeping downloaded files on disk. Confirmation includes a persistent **Don't ask me again** option, and the button disables itself when the list is empty.

## New Windows setup

- One **XDM-Setup.exe** selects x86 or x64 for the operating system.
- Optional parallel downloads of yt-dlp and FFmpeg, with percentage bars and verified SHA-256 hashes.
- Fixed-size wizard, actual MSI progress, optional desktop shortcut, and an explicit **Finish** button.
- Stable extension folders and personal data preserved outside the installer payload.
- Matching application, guide, and native SQLite dependency for each architecture.

Tested media: yt-dlp **2026.08.19** and FFmpeg **N-127043-g5a54fcf75e-20260930**. Media tools download during setup and are not embedded in the installer.

## Install or upgrade

Download **XDM-Setup.exe** below. Setup needs administrator rights, .NET Framework 4.7.2+, and internet access for selected media downloads.

Load the two extension folders under <code>C:\Program Files (x86)\XDM</code> once with **Load unpacked**. After upgrading, click **Reload** on both extensions. For full YouTube support, install **Deno 2.3.0+** separately and restart XDM.

See the [README](https://github.com/dimitris-lagos/xdm/blob/v9.0.2/README.md) for screenshots, setup steps, and silent-install options.

## Validation

Both architectures built successfully. SQLite read/write, FFmpeg decoding/HLS/merge/MP3, pinned yt-dlp version checks, 47 backend tests, and 42 extension tests passed. Cleanup UI layout and confirmation preference persistence were also checked.
