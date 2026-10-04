# XDM Download Controller

Load this directory as an unpacked extension in Opera or another Chromium browser.

The manifest key fixes the extension ID to `hmbfgklncdkaclckkibeflpmlaedhgck`. The XDM loopback controller accepts requests only from the matching `chrome-extension://` origin and requires a process-scoped session token for download snapshots and control actions.

The extension requests only the `alarms` and `storage` permissions and access to `http://127.0.0.1:8597/*`. Storage retains the scope preference, browser session start, download cache, event cursor, and badge state so worker restarts can resume processing.

Download and toolbar updates are pushed over an authenticated WebSocket at /controller/v1/events.
Lifecycle events are published after successful database mutations. Progress events are limited to
four per second per download; lifecycle events are never throttled. The backend retains the last
512 events for ordered replay. A cursor includes a process epoch and sequence number, and the worker
commits it with its activity state. A new backend process or expired cursor receives an atomic
snapshot; completed downloads in the current browser session are detected even if their start was
missed. Events older than the replay window that are no longer represented in the database cannot
be recovered.

Chrome/Chromium 116 or newer is required. A server heartbeat every 20 seconds keeps the worker
active; reconnection retries and an alarm recover a suspended worker. The popup subscribes to the
worker's updates, while initial/manual refresh and control commands still use HTTP. There is no
periodic download polling. Reload the extension and run the updated XDM backend to use this protocol.
