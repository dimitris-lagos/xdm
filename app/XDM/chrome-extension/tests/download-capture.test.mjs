import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = (await readFile(new URL('../app.js', import.meta.url), 'utf8'))
    .replace(/^import .*;\r?$/gm, '').replace('export default class App', 'class App');
const App = new Function(`${source}\nreturn App;`)();
test('three filename events hand off once, distinct downloads of same URL still work', () => {
    const instance = Object.create(App.prototype);
    instance.capturedDownloads = new Set();
    instance.logger = { log() {} };
    instance.isMonitoringEnabled = () => true;
    instance.shouldTakeOver = () => true;
    const sent = [];
    instance.triggerDownload = (...args) => sent.push(args);
    globalThis.chrome = { downloads: { cancel(id, cb) { cb(); }, erase() {} } };
    const item = { id: 1, startTime: '2026-09-30T12:00:00Z', url: 'https://example.test/a.zip', filename: 'a.zip' };
    for (let i = 0; i < 3; i++) instance.onDeterminingFilename(item, () => {});
    assert.equal(sent.length, 1);
    assert.equal(sent[0][5], '1:2026-09-30T12:00:00Z');
    instance.onDeterminingFilename({ ...item, id: 2 }, () => {});
    assert.equal(sent.length, 2);
    delete globalThis.chrome;
});
