"use strict";
import Logger from './logger.js';
import RequestWatcher from './request-watcher.js';
import Connector from './connector.js';
import { DEFAULT_FILTERS, filterMedia, normalizeFilters } from './media-filter.mjs';
import { CONNECTION_STATUS, ExtensionStateStore } from './extension-state.mjs';

export default class App {
    constructor() {
        this.logger = new Logger();
        this.blockedHosts = [];
        this.fileExts = [];
        this.requestWatcher = new RequestWatcher(this.onRequestDataReceived.bind(this));
        this.tabsWatcher = [];
        this.stateStore = new ExtensionStateStore();
        this.popupPorts = new Set();
        this.onDownloadCreatedCallback = this.onDownloadCreated.bind(this);
        this.onDeterminingFilenameCallback = this.onDeterminingFilename.bind(this);
        this.onTabUpdateCallback = this.onTabUpdate.bind(this);
        this.activeTabId = -1;
        this.mediaFilters = normalizeFilters(DEFAULT_FILTERS);
        this.connector = new Connector(this.onMessage.bind(this), this.onDisconnect.bind(this));
        this.stateStore.subscribe(snapshot => this.onStateChanged(snapshot));
    }

    start() {
        this.logger.log("starting...");
        this.starAppConnector();
        this.register();
        this.loadMediaFilters();
        this.logger.log("started.");
    }

    loadMediaFilters() {
        chrome.storage.local.get({ mediaFilters: DEFAULT_FILTERS }, result => {
            this.mediaFilters = normalizeFilters(result.mediaFilters);
            this.updateActionIcon();
        });
        chrome.storage.onChanged.addListener((changes, areaName) => {
            if (areaName === 'local' && changes.mediaFilters) {
                this.mediaFilters = normalizeFilters(changes.mediaFilters.newValue);
                this.updateActionIcon();
            }
        });
    }

    starAppConnector() {
        this.connector.connect();
    }

    onMessage(msg) {
        this.logger.log("message from XDM");
        this.logger.event('state.sync', {
            enabled: msg.enabled === true,
            mediaCount: Array.isArray(msg.videoList) ? msg.videoList.length : 0
        });
        this.fileExts = msg.fileExts;
        this.blockedHosts = msg.blockedHosts;
        this.tabsWatcher = msg.tabsWatcher;
        this.requestWatcher.updateConfig({
            mediaExts: msg.requestFileExts,
            blockedHosts: msg.blockedHosts,
            matchingHosts: msg.matchingHosts,
            mediaTypes: msg.mediaTypes
        });
        this.stateStore.applySync(msg);
    }

    onDisconnect() {
        this.logger.log("Disconnected from native host!");
        this.logger.log("Disconnected...");
        this.stateStore.setConnection(CONNECTION_STATUS.DISCONNECTED);
    }

    isMonitoringEnabled() {
        return this.stateStore.snapshot().monitoringEnabled;
    }

    onRequestDataReceived(data) {
        //Streaming video data received, send to native messaging application
        this.logger.log("onRequestDataReceived");
        this.logger.event('media.capture', {
            tabId: data.tabId,
            contentType: data.responseHeaders?.['Content-Type']?.[0] || '',
            contentLength: data.responseHeaders?.['Content-Length']?.[0] || 0
        });
        this.isMonitoringEnabled() && this.connector.isConnected() && this.connector.postMessage("/media", data);
    }

    onDeterminingFilename(download, suggest) {
        this.logger.log("onDeterminingFilename");
        if (!this.isMonitoringEnabled()) {
            return;
        }
        this.logger.log(download);
        let url = download.finalUrl || download.url;
        this.logger.log(url);
        if (this.isMonitoringEnabled() && this.shouldTakeOver(url, download.filename)) {
            chrome.downloads.cancel(
                download.id,
                () => chrome.downloads.erase({ id: download.id })
            );
            let referrer = download.referrer;
            if (!referrer && download.finalUrl !== download.url) {
                referrer = download.url;
            }
            this.triggerDownload(url, download.filename,
                referrer, download.fileSize, download.mime);
        }
    }

    onDownloadCreated(download) {
        this.logger.log("onDownloadCreated");
        this.logger.log(download);
    }

    onTabUpdate(tabId, changeInfo, tab) {
        if (!this.isMonitoringEnabled()) {
            return;
        }
        if (changeInfo.title) {
            if (this.tabsWatcher &&
                this.tabsWatcher.find(t => tab.url.indexOf(t) > 0)) {
                this.logger.log("Tab changed: " + changeInfo.title + " => " + tab.url);
                try {
                    this.connector.postMessage("/tab-update", {
                        tabUrl: tab.url,
                        tabTitle: changeInfo.title
                    });
                } catch (ex) {
                    console.log(ex);
                }
            }
        }
    }

    register() {
        chrome.downloads.onCreated.addListener(
            this.onDownloadCreatedCallback
        );
        chrome.downloads.onDeterminingFilename.addListener(
            this.onDeterminingFilenameCallback
        );
        chrome.tabs.onUpdated.addListener(
            this.onTabUpdateCallback
        );
        chrome.runtime.onMessage.addListener(this.onPopupMessage.bind(this));
        chrome.runtime.onConnect.addListener(this.onPopupConnected.bind(this));
        this.requestWatcher.register();
        this.attachContextMenu();
        chrome.tabs.onActivated.addListener(this.onTabActivated.bind(this));
    }

    isSupportedProtocol(url) {
        if (!url) return false;
        let u = new URL(url);
        return u.protocol === 'http:' || u.protocol === 'https:';
    }

    shouldTakeOver(url, file) {
        let u = new URL(url);
        if (!this.isSupportedProtocol(url)) {
            return false;
        }
        let hostName = u.host;
        if (this.blockedHosts.find(item => hostName.indexOf(item) >= 0)) {
            return false;
        }
        let path = file || u.pathname;
        let upath = path.toUpperCase();
        if (this.fileExts.find(ext => upath.endsWith(ext))) {
            return true;
        }
        return false;
    }

    updateActionIcon() {
        chrome.action.setIcon({ path: this.getActionIcon() });
        const state = this.stateStore.snapshot();
        let vc = "";
        if (state.list.length > 0) {
            let len = filterMedia(state.list, this.mediaFilters).length;
            if (len > 0) {
                vc = len + "";
            }
        }
        // if (this.videoList && this.videoList.length > 0) {
        //     let len = this.videoList.filter(vid => {
        //         if (!vid.tabId) {
        //             return true;
        //         }
        //         if (vid.tabId == '-1') {
        //             return true;
        //         }
        //         return (vid.tabId == this.activeTabId);
        //     }).length;
        //     if (len > 0) {
        //         vc = len + "";
        //     }
        // }
        chrome.action.setBadgeText({ text: vc });
    }

    getActionIconName(icon) {
        return this.isMonitoringEnabled() ? icon + ".png" : icon + "-mono.png";
    }

    getActionIcon() {
        return {
            "16": this.getActionIconName("icon16"),
            "48": this.getActionIconName("icon48"),
            "128": this.getActionIconName("icon128")
        }
    }

    triggerDownload(url, file, referer, size, mime) {
        chrome.cookies.getAll({ "url": url }, cookies => {
            let cookieStr = undefined;
            if (cookies) {
                cookieStr = cookies.map(cookie => cookie.name + "=" + cookie.value).join("; ");
            }
            let requestHeaders = { "User-Agent": [navigator.userAgent] };
            if (referer) {
                requestHeaders["Referer"] = [referer];
            }
            let responseHeaders = {};
            if (size) {
                let fz = +size;
                if (fz > 0) {
                    responseHeaders["Content-Length"] = [fz];
                }
            }
            if (mime) {
                responseHeaders["Content-Type"] = [mime];
            }
            let data = {
                url: url,
                cookie: cookieStr,
                requestHeaders: requestHeaders,
                responseHeaders: responseHeaders,
                filename: file,
                fileSize: size,
                mimeType: mime
            };
            this.logger.log(data);
            this.connector.postMessage("/download", data);
        });
    }

    diconnect() {
        this.onDisconnect();
    }

    onPopupMessage(request, sender, sendResponse) {
        this.logger.log(request.type);
        if (request.type === "stat") {
            sendResponse(this.stateStore.snapshot());
        }
        else if (request.type === "cmd") {
            this.logger.log("request.enabled:" + request.enabled);
            this.stateStore.setUserEnabled(request.enabled === true);
            if (request.enabled && !this.connector.isConnected()) {
                this.connector.refresh().catch(() => this.connector.launchApp());
            }
            sendResponse(this.stateStore.snapshot());
        }
        else if (request.type === "vid") {
            let vid = request.itemId;
            this.connector.postMessage("/vid", {
                vid: vid + "",
            }).then(() => sendResponse({ ok: true }))
                .catch(() => sendResponse({ ok: false }));
            return true;
        }
        else if (request.type === "clear") {
            this.connector.postMessage("/clear", {});
        }
    }

    onStateChanged(snapshot) {
        this.logger.event('state.publish', {
            revision: snapshot.revision,
            mediaRevision: snapshot.mediaRevision,
            connection: snapshot.connection,
            mediaCount: snapshot.list.length,
            subscribers: this.popupPorts.size
        });
        this.updateActionIcon();
        this.popupPorts.forEach(port => {
            try {
                port.postMessage(snapshot);
            } catch {
                this.popupPorts.delete(port);
            }
        });
    }

    onPopupConnected(port) {
        if (port.name !== 'xdm-popup-state') return;
        this.popupPorts.add(port);
        port.postMessage(this.stateStore.snapshot());
        this.connector.setInteractive(true);
        port.onDisconnect.addListener(() => {
            this.popupPorts.delete(port);
            this.connector.setInteractive(this.popupPorts.size > 0);
        });
    }

    sendLinkToXDM(info, tab) {
        let url = info.linkUrl;
        if (!this.isSupportedProtocol(url)) {
            url = info.srcUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            url = info.pageUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            return;
        }
        this.triggerDownload(url, null, info.pageUrl, null, null);
    }

    sendImageToXDM(info, tab) {
        let url = info.srcUrl;
        if (!this.isSupportedProtocol(url))
            url = info.linkUrl;
        if (!this.isSupportedProtocol(url)) {
            url = info.pageUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            return;
        }
        this.triggerDownload(url, null, info.pageUrl, null, null);
    }

    onMenuClicked(info, tab) {
        if (info.menuItemId == "download-any-link") {
            this.sendLinkToXDM(info, tab);
        }
        if (info.menuItemId == "download-image-link") {
            this.sendImageToXDM(info, tab);
        }
    }

    attachContextMenu() {
        chrome.contextMenus.create({
            id: 'download-any-link',
            title: "Download with XDM",
            contexts: ["link", "video", "audio", "all"]
        });

        chrome.contextMenus.create({
            id: 'download-image-link',
            title: "Download Image with XDM",
            contexts: ["image"]
        });

        chrome.contextMenus.onClicked.addListener(this.onMenuClicked.bind(this));
    }

    onTabActivated(activeInfo) {
        this.activeTabId = activeInfo.tabId + "";
        this.logger.log("Active tab: " + this.activeTabId);
        this.updateActionIcon();
    }
}
