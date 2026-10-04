import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { youtubeVideoUrl } from '../youtube.mjs';

const source = (await readFile(new URL('../app.js', import.meta.url), 'utf8'))
    .replace(/^import .*;\r?$/gm, '')
    .replace('export default class App', 'class App');
const App = new Function('youtubeVideoUrl', `${source}\nreturn App;`)(youtubeVideoUrl);
function app() {
    const instance = Object.create(App.prototype);
    instance.youtubeTabs = new Map();
    instance.tabsWatcher = ['.youtube.', '/watch?v='];
    instance.isMonitoringEnabled = () => true;
    instance.isYtdlpEnabled = () => true;
    instance.messages = [];
    instance.connector = {
        isConnected: () => true,
        postMessage: (path, data) => { instance.messages.push({ path, data }); return Promise.resolve(); }
    };
    return instance;
}

test('recognizes watch, short, live, embed and short-link pages', () => {
    for (const url of ['https://youtube.com/watch?v=jNQXAC9IVRw&t=5', 'https://youtu.be/jNQXAC9IVRw',
        'https://m.youtube.com/shorts/jNQXAC9IVRw', 'https://youtube.com/live/jNQXAC9IVRw',
        'https://youtube.com/embed/jNQXAC9IVRw'])
        assert.equal(youtubeVideoUrl(url), 'https://www.youtube.com/watch?v=jNQXAC9IVRw');
    assert.equal(youtubeVideoUrl('https://youtube.com.evil.test/watch?v=jNQXAC9IVRw'), null);
    assert.equal(youtubeVideoUrl('https://youtube.com/'), null);
});

test('reports URL changes without waiting for title and deduplicates title/load events', () => {
    const instance = app();
    const tab = { url: 'https://youtube.com/watch?v=jNQXAC9IVRw', title: 'clip' };
    instance.onTabUpdate(42, { url: tab.url }, tab);
    instance.onTabUpdate(42, { title: 'clip' }, tab);
    instance.onTabUpdate(42, { status: 'complete' }, tab);
    assert.equal(instance.messages.length, 1);
    assert.equal(instance.messages[0].data.tabId, '42');
    instance.onTabUpdate(42, { url: 'new' }, { url: 'https://youtube.com/watch?v=abcdefghijk' });
    assert.equal(instance.messages.length, 2);
});

test('navigation away invalidates pending extraction and disabled monitoring sends nothing', () => {
    const instance = app();
    instance.onTabUpdate(42, {}, { url: 'https://youtube.com/watch?v=jNQXAC9IVRw' });
    instance.onTabUpdate(42, { url: 'https://example.test/' }, { url: 'https://example.test/' });
    assert.equal(instance.messages.length, 2);
    assert.equal(instance.youtubeTabs.size, 0);
    instance.isMonitoringEnabled = () => false;
    instance.onTabUpdate(42, {}, { url: 'https://youtube.com/watch?v=jNQXAC9IVRw' });
    assert.equal(instance.messages.length, 2);
});

test("yt-dlp off prevents automatic requests while ordinary monitoring remains enabled", () => {
    const instance = app();
    instance.isYtdlpEnabled = () => false;
    instance.onTabUpdate(42, {}, { url: "https://youtube.com/watch?v=jNQXAC9IVRw" });
    assert.equal(instance.messages.length, 0);
    assert.equal(instance.youtubeTabs.size, 0);
    assert.equal(instance.isMonitoringEnabled(), true);
    instance.isYtdlpEnabled = () => true;
    instance.onTabUpdate(42, {}, { url: "https://youtube.com/watch?v=jNQXAC9IVRw" });
    assert.equal(instance.messages.length, 1);
});
