# XDM Download Controller

Load this directory as an unpacked extension in Opera or another Chromium browser.

The manifest key fixes the extension ID to `hmbfgklncdkaclckkibeflpmlaedhgck`. The XDM loopback controller accepts requests only from the matching `chrome-extension://` origin and requires a process-scoped session token for download snapshots and control actions.

The extension requests only the `alarms` and `storage` permissions and access to `http://127.0.0.1:8597/*`. Storage is used only for the Session / All download-history preference and the current Opera session start time.
