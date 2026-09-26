export const CONNECTION_STATUS = Object.freeze({
    CONNECTING: 'connecting',
    CONNECTED: 'connected',
    DISCONNECTED: 'disconnected'
});

function mediaSignature(items) {
    return JSON.stringify((items || []).map(item => [
        item.id,
        item.text,
        item.info,
        item.extension,
        item.quality,
        item.size,
        item.tabId
    ]));
}

export class ExtensionStateStore {
    constructor() {
        this.listeners = new Set();
        this.connection = CONNECTION_STATUS.CONNECTING;
        this.appEnabled = false;
        this.userDisabled = false;
        this.videoList = [];
        this.videoListSignature = mediaSignature(this.videoList);
        this.mediaRevision = 0;
        this.revision = 0;
    }

    subscribe(listener) {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }

    setConnection(connection) {
        if (this.connection === connection) return false;
        this.connection = connection;
        this.emit();
        return true;
    }

    setUserEnabled(enabled) {
        const userDisabled = enabled === false;
        if (this.userDisabled === userDisabled) return false;
        this.userDisabled = userDisabled;
        this.emit();
        return true;
    }

    applySync(payload = {}) {
        const nextList = Array.isArray(payload.videoList) ? payload.videoList : [];
        const nextSignature = mediaSignature(nextList);
        const nextAppEnabled = payload.enabled === true;
        const changed = this.appEnabled !== nextAppEnabled
            || this.videoListSignature !== nextSignature
            || this.connection !== CONNECTION_STATUS.CONNECTED;

        this.appEnabled = nextAppEnabled;
        if (this.videoListSignature !== nextSignature) {
            this.videoList = nextList;
            this.videoListSignature = nextSignature;
            this.mediaRevision += 1;
        }
        this.connection = CONNECTION_STATUS.CONNECTED;
        if (changed) this.emit();
        return changed;
    }

    snapshot() {
        const connected = this.connection === CONNECTION_STATUS.CONNECTED;
        return {
            revision: this.revision,
            mediaRevision: this.mediaRevision,
            connection: this.connection,
            connected,
            appEnabled: this.appEnabled,
            userEnabled: !this.userDisabled,
            monitoringEnabled: connected && this.appEnabled && !this.userDisabled,
            list: this.videoList
        };
    }

    emit() {
        this.revision += 1;
        const snapshot = this.snapshot();
        this.listeners.forEach(listener => listener(snapshot));
    }
}
