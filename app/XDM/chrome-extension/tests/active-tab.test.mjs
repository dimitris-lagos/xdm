import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { ExtensionStateStore } from '../extension-state.mjs';
import { filterMedia } from '../media-filter.mjs';

const source = (await readFile(new URL('../app.js', import.meta.url), 'utf8'))
    .replace(/^import .*;\r?$/gm, '')
    .replace('export default class App', 'class App');
const App = new Function('filterMedia', `${source}\nreturn App;`)(filterMedia);

function fixture() {
    const instance = Object.create(App.prototype);
    instance.stateStore = new ExtensionStateStore();
    instance.activeTabQueryGeneration = 0;
    const queries = [];
    const badges = [];
    globalThis.chrome = {
        tabs: { query: (options, callback) => queries.push({ options, callback }) },
        runtime: {},
        action: { setIcon: () => {}, setBadgeText: value => badges.push(value.text) }
    };
    return { instance, queries, badges };
}

test('resolves focused-window active tab on worker wake and ignores an older query result', () => {
    const { instance, queries } = fixture();
    instance.refreshActiveTab();
    instance.onTabActivated({ tabId: 42 });
    assert.deepEqual(queries[0].options, { active: true, lastFocusedWindow: true });
    queries[1].callback([{ id: 42 }]);
    queries[0].callback([{ id: 41 }]);
    assert.equal(instance.stateStore.snapshot().activeTabId, '42');
    instance.refreshActiveTab();
    queries[2].callback([]);
    assert.deepEqual(instance.stateStore.snapshot().list, []);
});

test('badge counts only current-tab media after applying media filters', () => {
    const { instance, badges } = fixture();
    instance.mediaFilters = { videoCodecs: ['av1'] };
    instance.stateStore.applySync({ enabled: true, videoList: [
        { id: 'a', tabId: '41', extension: 'mp4', quality: '1080p av01.0.08M.08' },
        { id: 'b', tabId: '41', extension: 'mp4', quality: '1080p avc1.640028' },
        { id: 'c', tabId: '42', extension: 'mp4', quality: '1080p av01.0.08M.08' }
    ] });
    instance.stateStore.setActiveTab(41);
    instance.updateActionIcon();
    assert.equal(badges.at(-1), '1');
    instance.stateStore.setActiveTab(99);
    instance.updateActionIcon();
    assert.equal(badges.at(-1), '');
});
