import assert from "node:assert/strict";
import test from "node:test";

test("worker serializes a burst, persists cursor, reconnects and ignores replay duplicates", async () => {
  const saved = { chrome: globalThis.chrome, WebSocket: globalThis.WebSocket, fetch: globalThis.fetch,
    setTimeout: globalThis.setTimeout, clearTimeout: globalThis.clearTimeout };
  const timers = new Map();
  let nextTimer = 0;
  const stored = { browserSessionStartedAt: Date.parse("2026-10-03T10:00:00Z") };
  const badges = [];
  const sockets = [];
  const handlers = {};
  const turn = () => new Promise(resolve => setImmediate(resolve));
  async function flush() { for (let i = 0; i < 30; i++) await turn(); }
  class Socket {
    static CLOSING = 2;
    constructor(url, protocols) { this.url = url; this.protocols = protocols; this.readyState = 0; sockets.push(this); }
    close() { this.readyState = 3; this.onclose?.(); }
  }
  try {
    globalThis.setTimeout = (fn, delay) => { const id = ++nextTimer; timers.set(id, { fn, delay }); return id; };
    globalThis.clearTimeout = id => timers.delete(id);
    globalThis.WebSocket = Socket;
    globalThis.fetch = async () => ({ ok: true, status: 200, json: async () => ({ token: "secret" }) });
    globalThis.chrome = {
      storage: { local: {
        get: async keys => {
          await turn();
          const names = typeof keys === "string" ? [keys] : keys;
          return Object.fromEntries(names.map(key => [key, structuredClone(stored[key])]));
        },
        set: async values => { await turn(); Object.assign(stored, structuredClone(values)); }
      } },
      action: {
        setBadgeText: async ({ text }) => { badges.push(text); },
        setBadgeBackgroundColor: async () => {}, setTitle: async () => {}
      },
      alarms: { create() {}, clear() {}, onAlarm: { addListener(fn) { handlers.alarm = fn; } } },
      runtime: {
        sendMessage: async () => {},
        onInstalled: { addListener() {} }, onStartup: { addListener() {} },
        onMessage: { addListener(fn) { handlers.message = fn; } }
      }
    };
    await import("../service-worker.js");
    await flush();
    assert.equal(sockets.length, 1);
    const socket = sockets[0];
    socket.readyState = 1;
    socket.onopen();
    await flush();
    const emit = (sequence, state, kind) => socket.onmessage({ data: JSON.stringify({
      type: "downloads", epoch: "process", sequence, delta: true, kind, id: "tiny",
      downloads: [{ id: "tiny", state, dateAdded: "2026-10-03T10:00:01Z" }]
    }) });
    emit(1, "Downloading", "started");
    emit(2, "Finished", "finished");
    await flush();
    assert.ok(badges.includes("1"));
    assert.equal(badges.at(-1), "✓");
    assert.equal(stored.controllerEventCursor.sequence, 2);
    emit(2, "Downloading", "started");
    await flush();
    assert.equal(stored.controllerDownloads[0].state, "Finished");
    socket.close();
    await flush();
    assert.equal(badges.at(-1), "!");
    const retry = [...timers.values()].find(timer => timer.delay === 2000);
    assert.ok(retry);
    retry.fn();
    await flush();
    assert.equal(sockets.length, 2);
    assert.match(sockets[1].url, /epoch=process&after=2/);
    sockets[1].readyState = 1;
    sockets[1].onopen();
    await flush();
    assert.equal(badges.at(-1), "✓");
    handlers.message({ type: "controller-opened" });
    await flush();
    assert.equal(badges.at(-1), "");
  } finally { Object.assign(globalThis, saved); }
});
