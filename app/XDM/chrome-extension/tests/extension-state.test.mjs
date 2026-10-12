import test from 'node:test';
import assert from 'node:assert/strict';

import { CONNECTION_STATUS, ExtensionStateStore } from '../extension-state.mjs';

test('publishes connection changes and derives monitoring state centrally', () => {
    const store = new ExtensionStateStore();
    const snapshots = [];
    store.subscribe(state => snapshots.push(state));

    store.setConnection(CONNECTION_STATUS.DISCONNECTED);
    store.applySync({ enabled: true, videoList: [] });
    store.setUserEnabled(false);

    assert.equal(snapshots[0].connection, CONNECTION_STATUS.DISCONNECTED);
    assert.equal(snapshots[1].monitoringEnabled, true);
    assert.equal(snapshots[2].monitoringEnabled, false);
});

test('does not repaint subscribers for identical sync payloads', () => {
    const store = new ExtensionStateStore();
    let notifications = 0;
    store.subscribe(() => notifications += 1);
    const payload = {
        enabled: true,
        videoList: [{ id: '1', text: 'clip.mp4', quality: '1080p' }]
    };

    assert.equal(store.applySync(payload), true);
    assert.equal(store.applySync(structuredClone(payload)), false);
    assert.equal(notifications, 1);
    assert.equal(store.snapshot().mediaRevision, 1);
});

test('increments media revision only when renderable media changes', () => {
    const store = new ExtensionStateStore();
    store.applySync({ enabled: true, videoList: [] });
    assert.equal(store.snapshot().mediaRevision, 0);

    store.applySync({ enabled: true, videoList: [{ id: '1', text: 'clip.mp4' }] });
    assert.equal(store.snapshot().mediaRevision, 1);
    store.setConnection(CONNECTION_STATUS.DISCONNECTED);
    assert.equal(store.snapshot().mediaRevision, 1);
});

test('shows only active-tab media and excludes unassigned captures', () => {
    const store = new ExtensionStateStore();
    store.applySync({ enabled: true, videoList: [
        { id: 'first', tabId: '41' }, { id: 'second', tabId: 42 },
        { id: 'unassigned' }, { id: 'unknown', tabId: '-1' }
    ] });
    assert.deepEqual(store.snapshot().list, []);
    store.setActiveTab(41);
    assert.deepEqual(store.snapshot().list.map(item => item.id), ['first']);
    store.setActiveTab('42');
    assert.deepEqual(store.snapshot().list.map(item => item.id), ['second']);
    store.setActiveTab(99);
    assert.deepEqual(store.snapshot().list, []);
    store.setActiveTab(-1);
    assert.deepEqual(store.snapshot().list, []);
    assert.equal(store.videoList.length, 4);
});

test('switching tabs publishes a fresh media revision without waiting for a sync', () => {
    const store = new ExtensionStateStore();
    store.setActiveTab(1);
    store.applySync({ enabled: true, videoList: [{ id: 'first', tabId: 1 }, { id: 'second', tabId: 2 }] });
    const before = store.snapshot().mediaRevision;
    const snapshots = [];
    store.subscribe(state => snapshots.push(state));
    store.setActiveTab(2);
    assert.equal(snapshots.length, 1);
    assert.equal(snapshots[0].mediaRevision, before + 1);
    assert.equal(snapshots[0].activeTabId, '2');
    assert.deepEqual(snapshots[0].list.map(item => item.id), ['second']);
    assert.equal(store.setActiveTab('2'), false);
    assert.equal(snapshots.length, 1);
});
