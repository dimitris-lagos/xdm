import {
    DEFAULT_FILTERS,
    availableFilterOptions,
    filterMedia,
    formatMediaDetails,
    normalizeFilters
} from './media-filter.mjs';

class VideoPopup {
    constructor() {
        this.selectedIds = new Set();
        this.submittedIds = new Set();
        this.allItems = [];
        this.filters = normalizeFilters(DEFAULT_FILTERS);
        this.connected = false;
        this.lastMediaRevision = -1;
        this.pendingState = null;
        this.renderFrame = null;
    }

    run() {
        document.addEventListener('DOMContentLoaded', this.onLoad.bind(this), false);
    }

    onLoad() {
        this.filterButton = document.getElementById('filters');
        this.filterPanel = document.getElementById('filter-panel');
        this.monitoringToggle = document.getElementById('chk');
        this.monitoringStatus = document.getElementById('monitoring-status');
        this.monitoringLabel = document.getElementById('monitoring-label');
        this.downloadCheckedButton = document.getElementById('download-checked');
        this.videoQuality = document.getElementById('video-quality');
        this.audioQuality = document.getElementById('audio-quality');
        this.minimumSize = document.getElementById('minimum-size');

        this.filterButton.addEventListener('click', () => {
            const willOpen = this.filterPanel.hidden;
            this.filterPanel.hidden = !willOpen;
            this.filterButton.classList.toggle('active', willOpen);
            this.filterButton.setAttribute('aria-expanded', String(willOpen));
        });

        this.monitoringToggle.addEventListener('change', () => {
            const enabled = this.monitoringToggle.checked;
            this.monitoringToggle.disabled = true;
            chrome.runtime.sendMessage({ type: 'cmd', enabled }, state => this.queueState(state));
        });

        this.downloadCheckedButton.addEventListener('click', () => this.downloadSelected());

        document.getElementById('clear').addEventListener('click', () => {
            chrome.runtime.sendMessage({ type: 'clear' });
            window.close();
        });

        document.getElementById('format').addEventListener('click', () => {
            alert('Please play the video in the desired format in the web player.');
        });

        this.videoQuality.addEventListener('change', () => this.readAndApplyFilters());
        this.audioQuality.addEventListener('change', () => this.readAndApplyFilters());
        this.minimumSize.addEventListener('input', () => this.readAndApplyFilters());
        document.getElementById('reset-filters').addEventListener('click', () => {
            this.filters = normalizeFilters(DEFAULT_FILTERS);
            this.syncFilterControls();
            this.persistFilters();
            this.applyFilters();
        });

        chrome.storage.local.get({ mediaFilters: DEFAULT_FILTERS }, result => {
            this.filters = normalizeFilters(result.mediaFilters);
            this.syncFilterControls();
            this.connectToState();
        });
    }

    connectToState() {
        this.statePort = chrome.runtime.connect({ name: 'xdm-popup-state' });
        this.statePort.onMessage.addListener(state => this.queueState(state));
        this.statePort.onDisconnect.addListener(() => this.queueState({
            connection: 'disconnected',
            connected: false,
            appEnabled: false,
            monitoringEnabled: false,
            mediaRevision: this.lastMediaRevision,
            list: this.allItems
        }));
        chrome.runtime.sendMessage({ type: 'stat' }, state => this.queueState(state));
    }

    queueState(state) {
        if (!state) return;
        this.pendingState = state;
        if (this.renderFrame !== null) return;
        this.renderFrame = requestAnimationFrame(() => {
            this.renderFrame = null;
            const nextState = this.pendingState;
            this.pendingState = null;
            this.applyState(nextState);
        });
    }

    applyState(state) {
        const renderStarted = performance.now();
        const wasConnected = this.connected;
        this.connected = state.connected === true;
        this.monitoringToggle.checked = state.monitoringEnabled === true;
        this.monitoringToggle.disabled = !this.connected || state.appEnabled !== true;
        this.updateMonitoringState(state);

        const mediaChanged = this.lastMediaRevision !== state.mediaRevision;
        if (mediaChanged) {
            this.lastMediaRevision = state.mediaRevision;
            this.allItems = Array.isArray(state.list) ? state.list : [];
            this.reconcileFiltersWithAvailableMedia();
            this.renderExtensionFilters();
            this.renderQualityFilters();
            this.syncFilterControls();
            this.persistFilters();
        }
        if (mediaChanged || wasConnected !== this.connected) this.applyFilters();
        console.debug('[XDM UI] state.render', {
            revision: state.revision,
            mediaRevision: state.mediaRevision,
            mediaChanged,
            mediaCount: this.allItems.length,
            durationMs: Math.round((performance.now() - renderStarted) * 100) / 100
        });
    }

    updateMonitoringState(state) {
        let label = 'Off';
        let status = 'Browser monitoring off';
        if (state.connection === 'connecting') {
            label = '…';
            status = 'Connecting to XDM…';
        } else if (!state.connected) {
            status = 'XDM is offline';
        } else if (!state.appEnabled) {
            status = 'Monitoring disabled in XDM';
        } else if (state.monitoringEnabled) {
            label = 'On';
            status = 'Browser monitoring on';
        }
        this.monitoringLabel.textContent = label;
        this.monitoringStatus.textContent = status;
        this.monitoringStatus.classList.toggle('online', state.monitoringEnabled === true);
        this.monitoringStatus.classList.toggle('offline', state.connection !== 'connecting' && state.monitoringEnabled !== true);
    }

    renderExtensionFilters() {
        const container = document.getElementById('extension-filters');
        container.replaceChildren();

        availableFilterOptions(this.allItems).extensions.forEach(extension => {
            const label = document.createElement('label');
            label.className = 'filter-chip';
            const input = document.createElement('input');
            input.type = 'checkbox';
            input.value = extension;
            input.checked = this.filters.extensions.includes(extension);
            input.addEventListener('change', () => this.readAndApplyFilters());
            const text = document.createElement('span');
            text.textContent = extension;
            label.append(input, text);
            container.appendChild(label);
        });
    }

    reconcileFiltersWithAvailableMedia() {
        const options = availableFilterOptions(this.allItems);
        this.filters = normalizeFilters({
            extensions: this.filters.extensions.filter(value => options.extensions.includes(value)),
            videoQuality: options.videoQualities.includes(this.filters.videoQuality) ? this.filters.videoQuality : '',
            audioQuality: options.audioQualities.includes(this.filters.audioQuality) ? this.filters.audioQuality : '',
            minimumSizeMb: this.filters.minimumSizeMb
        });
    }

    renderQualityFilters() {
        const options = availableFilterOptions(this.allItems);
        this.replaceSelectOptions(this.videoQuality, options.videoQualities, value => `${value}p`);
        this.replaceSelectOptions(this.audioQuality, options.audioQualities, value => `${value} Kbps`);
    }

    replaceSelectOptions(select, values, formatLabel) {
        select.replaceChildren(new Option('Any', ''));
        values.forEach(value => select.add(new Option(formatLabel(value), value)));
    }

    syncFilterControls() {
        this.videoQuality.value = this.filters.videoQuality;
        this.audioQuality.value = this.filters.audioQuality;
        this.minimumSize.value = String(this.filters.minimumSizeMb);
        document.querySelectorAll('#extension-filters input').forEach(input => {
            input.checked = this.filters.extensions.includes(input.value);
        });
    }

    readAndApplyFilters() {
        const extensions = Array.from(document.querySelectorAll('#extension-filters input:checked'))
            .map(input => input.value);
        this.filters = normalizeFilters({
            extensions,
            videoQuality: this.videoQuality.value,
            audioQuality: this.audioQuality.value,
            minimumSizeMb: this.minimumSize.value
        });
        this.persistFilters();
        this.applyFilters();
    }

    persistFilters() {
        chrome.storage.local.set({ mediaFilters: this.filters });
    }

    applyFilters() {
        const visibleItems = filterMedia(this.allItems, this.filters);
        this.renderList(visibleItems);
        this.updateFilterResult(visibleItems.length);
    }

    updateFilterResult(visibleCount) {
        const total = this.allItems.length;
        const active = this.filters.extensions.length > 0
            || this.filters.videoQuality
            || this.filters.audioQuality
            || this.filters.minimumSizeMb > 0;
        const result = document.getElementById('filter-result');
        result.textContent = active ? `Showing ${visibleCount} of ${total} detected media` : 'Showing all detected media';
        this.filterButton.classList.toggle('has-filters', Boolean(active));
    }

    updateSelectionState() {
        const count = this.selectedIds.size;
        this.downloadCheckedButton.disabled = count === 0;
        this.downloadCheckedButton.textContent = count > 0 ? `Download checked (${count})` : 'Download checked';
        this.downloadCheckedButton.classList.remove('sent', 'failed');
    }

    sendMedia(itemId) {
        return new Promise((resolve, reject) => {
            if (!this.connected) {
                reject(new Error('XDM is offline'));
                return;
            }
            chrome.runtime.sendMessage({ type: 'vid', itemId }, response => {
                if (chrome.runtime.lastError || response?.ok !== true) {
                    reject(new Error(chrome.runtime.lastError?.message || 'XDM did not accept the download'));
                    return;
                }
                resolve();
            });
        });
    }

    async downloadSelected() {
        const itemIds = Array.from(this.selectedIds);
        if (itemIds.length === 0) return;

        this.downloadCheckedButton.disabled = true;
        this.downloadCheckedButton.classList.add('sending');
        this.downloadCheckedButton.textContent = `Sending ${itemIds.length}…`;
        try {
            await Promise.all(itemIds.map(itemId => this.sendMedia(itemId)));
            itemIds.forEach(itemId => {
                this.selectedIds.delete(itemId);
                this.submittedIds.add(itemId);
            });
            this.renderList(filterMedia(this.allItems, this.filters));
            this.downloadCheckedButton.classList.add('sent');
            this.downloadCheckedButton.textContent = `Sent ${itemIds.length} ✓`;
            setTimeout(() => this.updateSelectionState(), 1200);
        } catch (error) {
            this.downloadCheckedButton.classList.add('failed');
            this.downloadCheckedButton.textContent = 'Send failed';
            this.downloadCheckedButton.disabled = false;
        } finally {
            this.downloadCheckedButton.classList.remove('sending');
        }
    }

    renderList(items) {
        const list = document.getElementById('list');
        const empty = document.getElementById('empty');
        const footer = document.getElementById('footer-actions');
        const template = document.getElementById('media-template');

        list.replaceChildren();
        const visibleIds = new Set(items.map(item => String(item.id)));
        this.selectedIds = new Set(Array.from(this.selectedIds).filter(itemId => visibleIds.has(itemId)));
        empty.hidden = items.length > 0;
        empty.textContent = this.allItems.length > 0 ? 'No media matches these filters.' : 'No media detected on this page.';
        footer.hidden = items.length === 0;

        [...items].reverse().forEach(item => {
            const card = template.content.firstElementChild.cloneNode(true);
            const checkbox = card.querySelector('.media-checkbox');
            const downloadButton = card.querySelector('.download-button');
            const itemId = String(item.id);
            const submitted = this.submittedIds.has(itemId);
            const details = formatMediaDetails(item);

            card.querySelector('.media-name').textContent = item.text || 'Detected media';
            card.querySelector('.media-info').textContent = details;
            checkbox.setAttribute('aria-label', `Select ${item.text || 'detected media'}`);
            checkbox.checked = this.selectedIds.has(itemId);
            checkbox.disabled = submitted || !this.connected;
            card.classList.toggle('submitted', submitted);
            if (submitted) {
                downloadButton.textContent = 'Sent ✓';
                downloadButton.disabled = true;
            } else if (!this.connected) {
                downloadButton.disabled = true;
            }

            checkbox.addEventListener('change', () => {
                if (checkbox.checked) {
                    this.selectedIds.add(itemId);
                } else {
                    this.selectedIds.delete(itemId);
                }
                this.updateSelectionState();
            });

            downloadButton.addEventListener('click', async () => {
                downloadButton.disabled = true;
                downloadButton.textContent = 'Sending…';
                try {
                    await this.sendMedia(itemId);
                    this.submittedIds.add(itemId);
                    this.selectedIds.delete(itemId);
                    card.classList.add('submitted');
                    checkbox.checked = false;
                    checkbox.disabled = true;
                    downloadButton.textContent = 'Sent ✓';
                    this.updateSelectionState();
                } catch (error) {
                    downloadButton.disabled = false;
                    downloadButton.textContent = 'Retry';
                }
            });

            list.appendChild(card);
        });

        this.updateSelectionState();
    }
}

new VideoPopup().run();
