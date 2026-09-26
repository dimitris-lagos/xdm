"use strict";
import Logger from './logger.js';

const APP_BASE_URL = "http://127.0.0.1:8597";
const SYNC_ALARM = 'xdm-state-sync';
const INTERACTIVE_SYNC_MS = 2000;

export default class Connector {
    constructor(onMessage, onDisconnect) {
        this.logger = new Logger();
        this.onMessage = onMessage;
        this.onDisconnect = onDisconnect;
        this.connected = undefined;
        this.interactive = false;
        this.inFlight = null;
        this.interactiveTimer = null;
        this.onAlarm = this.onAlarm.bind(this);
    }

    connect() {
        chrome.alarms.onAlarm.addListener(this.onAlarm);
        chrome.alarms.create(SYNC_ALARM, {
            delayInMinutes: 0.5,
            periodInMinutes: 0.5
        });
        this.refresh().catch(() => {});
    }

    onAlarm(alarm) {
        if (alarm.name === SYNC_ALARM) this.refresh().catch(() => {});
    }

    setInteractive(interactive) {
        this.interactive = interactive;
        if (!interactive) {
            clearTimeout(this.interactiveTimer);
            this.interactiveTimer = null;
            return;
        }
        this.refresh().catch(() => {});
        this.scheduleInteractiveRefresh();
    }

    scheduleInteractiveRefresh() {
        clearTimeout(this.interactiveTimer);
        if (!this.interactive) return;
        this.interactiveTimer = setTimeout(async () => {
            try {
                await this.refresh();
            } catch {
                // Connection state is published by refresh().
            }
            this.scheduleInteractiveRefresh();
        }, INTERACTIVE_SYNC_MS);
    }

    refresh() {
        if (this.inFlight) return this.inFlight;
        this.inFlight = fetch(APP_BASE_URL + "/sync", { cache: 'no-store' })
            .then(response => this.readResponse(response))
            .then(json => {
                this.connected = true;
                this.onMessage(json);
                return json;
            })
            .catch(error => {
                this.disconnect();
                throw error;
            })
            .finally(() => {
                this.inFlight = null;
            });
        return this.inFlight;
    }

    disconnect() {
        if (!this.connected) return;
        this.connected = false;
        this.onDisconnect();
    }

    isConnected() {
        return this.connected;
    }

    async readResponse(response) {
        if (!response.ok) throw new Error(`XDM returned HTTP ${response.status}`);
        return response.json();
    }

    postMessage(url, data) {
        return fetch(APP_BASE_URL + url, { method: "POST", body: JSON.stringify(data) })
            .then(async res => {
                if (!res.ok) throw new Error(`XDM returned HTTP ${res.status}`);
                this.connected = true;
                const json = await res.json();
                this.onMessage(json);
                return json;
            })
            .catch(err => {
                this.disconnect();
                throw err;
            });
    }

    launchApp() {

    }
}
