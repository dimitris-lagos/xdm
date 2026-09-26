export const DEFAULT_FILTERS = Object.freeze({
    extensions: [],
    videoQuality: '',
    audioQuality: '',
    minimumSizeMb: 0
});

const AUDIO_EXTENSIONS = new Set(['mp3', 'm4a', 'aac', 'ogg', 'opus', 'wav', 'flac', 'weba']);
const VIDEO_EXTENSIONS = new Set(['mp4', 'mkv', 'webm', 'avi', 'mov', 'flv', 'ts', 'm4v']);

function firstMatch(text, expression) {
    const match = expression.exec(text);
    return match ? match[1].toLowerCase() : '';
}

function parseSize(text) {
    const match = /(?:^|\s)(\d+(?:[.,]\d+)?)\s*(KiB|MiB|GiB|KB|MB|GB)\b/i.exec(text);
    if (!match) return 0;

    const value = Number(match[1].replace(',', '.'));
    const unit = match[2].toUpperCase();
    const multipliers = {
        KIB: 1024,
        MIB: 1024 ** 2,
        GIB: 1024 ** 3,
        KB: 1000,
        MB: 1000 ** 2,
        GB: 1000 ** 3
    };
    return Number.isFinite(value) ? Math.round(value * multipliers[unit]) : 0;
}

function parseVideoQuality(text) {
    return firstMatch(text, /\b(\d{3,4})\s*p\b/i)
        || firstMatch(text, /\b(\d{3,4})\s*$/i)
        || firstMatch(text, /\b\d{2,5}\s*x\s*(\d{3,4})\b/i);
}

export function normalizeMedia(item) {
    const text = String(item.text || '');
    const info = String(item.info || '');
    const quality = String(item.quality || info);
    const searchable = `${text} ${info} ${quality}`;
    const extension = String(item.extension || '').replace(/^\./, '').toLowerCase()
        || firstMatch(text, /\.([a-z0-9]{2,5})(?:$|\s)/i)
        || firstMatch(searchable, /\[([a-z0-9]{2,5})(?:\s+AUDIO)?\]/i);
    const videoQuality = parseVideoQuality(searchable);
    const audioQuality = firstMatch(searchable, /\b(\d{2,4})\s*k(?:bit(?:\/s)?|bps|b\/s)?\b/i);
    const explicitSize = Number(item.size);
    const size = Number.isFinite(explicitSize) && explicitSize > 0 ? explicitSize : parseSize(searchable);
    const audioOnly = /\bAUDIO\b/i.test(searchable) || AUDIO_EXTENSIONS.has(extension);
    const kind = audioOnly ? 'audio' : (VIDEO_EXTENSIONS.has(extension) || videoQuality ? 'video' : 'other');

    return {
        ...item,
        extension,
        videoQuality,
        audioQuality,
        size,
        kind
    };
}

export function normalizeFilters(filters = {}) {
    return {
        extensions: Array.isArray(filters.extensions)
            ? [...new Set(filters.extensions.map(value => String(value).toLowerCase()).filter(Boolean))]
            : [],
        videoQuality: String(filters.videoQuality || ''),
        audioQuality: String(filters.audioQuality || ''),
        minimumSizeMb: Math.max(0, Number(filters.minimumSizeMb) || 0)
    };
}

export function matchesMediaFilters(media, rawFilters) {
    const filters = normalizeFilters(rawFilters);
    if (filters.extensions.length > 0 && !filters.extensions.includes(media.extension)) return false;
    if (filters.videoQuality && media.kind === 'video' && media.videoQuality !== filters.videoQuality) return false;
    if (filters.audioQuality && media.kind === 'audio' && media.audioQuality !== filters.audioQuality) return false;

    if (filters.minimumSizeMb > 0) {
        const minimumBytes = filters.minimumSizeMb * 1024 * 1024;
        if (!media.size || media.size <= minimumBytes) return false;
    }

    return true;
}

export function filterMedia(items, filters) {
    return items.map(normalizeMedia).filter(media => matchesMediaFilters(media, filters));
}

function uniqueSorted(values) {
    return [...new Set(values.filter(Boolean))].sort((left, right) => {
        const numericDifference = Number(left) - Number(right);
        return Number.isFinite(numericDifference) && numericDifference !== 0
            ? numericDifference
            : String(left).localeCompare(String(right));
    });
}

export function availableFilterOptions(items) {
    const media = items.map(normalizeMedia);
    return {
        extensions: uniqueSorted(media.map(item => item.extension)),
        videoQualities: uniqueSorted(media.filter(item => item.kind === 'video').map(item => item.videoQuality)),
        audioQualities: uniqueSorted(media.filter(item => item.kind === 'audio').map(item => item.audioQuality))
    };
}

export function availableExtensions(items) {
    return availableFilterOptions(items).extensions;
}

export function formatMediaSize(bytes) {
    if (!bytes || bytes <= 0) return '';
    if (bytes >= 1024 ** 3) return `${(bytes / (1024 ** 3)).toFixed(1)} GiB`;
    if (bytes >= 1024 ** 2) return `${(bytes / (1024 ** 2)).toFixed(1)} MiB`;
    return `${Math.round(bytes / 1024)} KiB`;
}

export function formatMediaDetails(item) {
    const media = normalizeMedia(item);
    let description = String(item.info || item.quality || '').trim();
    if (media.size > 0) {
        description = description
            .replace(/\s+\d+(?:[.,]\d+)?\s*(?:KiB|MiB|GiB|KB|MB|GB|[KMG])(?=\s|$)/ig, '')
            .trim();
    }
    return [description, formatMediaSize(media.size)]
        .filter((value, index, values) => value && values.indexOf(value) === index)
        .join(' · ');
}
