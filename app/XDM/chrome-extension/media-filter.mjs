export const DEFAULT_FILTERS = Object.freeze({
    extensions: [],
    videoCodecs: [],
    audioCodecs: [],
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

export function normalizeVideoCodec(value) {
    const codec = String(value || '').trim().toLowerCase();
    if (/\b(?:avc[13]|h[.\s-]?264)(?:\b|\.)/.test(codec)) return 'h264';
    if (/\b(?:hev1|hvc1|hevc|h[.\s-]?265)(?:\b|\.)/.test(codec)) return 'hevc';
    if (/\b(?:av01|av1)(?:\b|\.)/.test(codec)) return 'av1';
    if (/\b(?:vp09|vp9)(?:\b|\.)/.test(codec)) return 'vp9';
    if (/\b(?:vp08|vp8)(?:\b|\.)/.test(codec)) return 'vp8';
    return '';
}

export function videoCodecLabel(codec) {
    return { h264: 'H.264 (AVC)', hevc: 'H.265 (HEVC)', av1: 'AV1', vp9: 'VP9', vp8: 'VP8' }[codec] || codec;
}

export function normalizeAudioCodec(value) {
    const codec = String(value || '').trim().toLowerCase();
    if (/\b(?:mp4a(?:\.40)?|aac)(?:\b|\.)/.test(codec)) return 'aac';
    if (/\b(?:ec-?3|e-ac-?3|eac3)(?:\b|\.)/.test(codec)) return 'eac3';
    if (/\b(?:ac-?3)(?:\b|\.)/.test(codec)) return 'ac3';
    for (const name of ['opus', 'vorbis', 'mp3', 'flac', 'alac']) {
        if (new RegExp(`\\b${name}\\b`).test(codec)) return name;
    }
    if (/\bpcm(?:\b|_)/.test(codec)) return 'pcm';
    return '';
}

export function audioCodecLabel(codec) {
    return { aac: 'AAC', opus: 'Opus', vorbis: 'Vorbis', mp3: 'MP3', flac: 'FLAC',
        alac: 'ALAC', ac3: 'AC-3', eac3: 'E-AC-3', pcm: 'PCM' }[codec] || codec;
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

    const videoCodec = kind === 'video' ? normalizeVideoCodec(item.videoCodec || quality) : '';
    const audioCodec = kind === 'audio' ? normalizeAudioCodec(item.audioCodec || quality) : '';

    return {
        ...item,
        extension,
        videoQuality,
        videoCodec,
        audioQuality,
        audioCodec,
        size,
        kind
    };
}

export function normalizeFilters(filters = {}) {
    return {
        extensions: Array.isArray(filters.extensions)
            ? [...new Set(filters.extensions.map(value => String(value).toLowerCase()).filter(Boolean))]
            : [],
        videoCodecs: Array.isArray(filters.videoCodecs)
            ? [...new Set(filters.videoCodecs.map(normalizeVideoCodec).filter(Boolean))]
            : [],
        audioCodecs: Array.isArray(filters.audioCodecs)
            ? [...new Set(filters.audioCodecs.map(normalizeAudioCodec).filter(Boolean))]
            : [],
        videoQuality: String(filters.videoQuality || ''),
        audioQuality: String(filters.audioQuality || ''),
        minimumSizeMb: Math.max(0, Number(filters.minimumSizeMb) || 0)
    };
}

export function matchesMediaFilters(media, rawFilters) {
    const filters = normalizeFilters(rawFilters);
    if (filters.extensions.length > 0 && !filters.extensions.includes(media.extension)) return false;
    if (filters.videoCodecs.length > 0 && media.kind === 'video' && !filters.videoCodecs.includes(media.videoCodec)) return false;
    if (filters.videoQuality && media.kind === 'video' && media.videoQuality !== filters.videoQuality) return false;
    if (filters.audioCodecs.length > 0 && media.kind === 'audio' && !filters.audioCodecs.includes(media.audioCodec)) return false;
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
        videoCodecs: uniqueSorted(media.filter(item => item.kind === 'video').map(item => item.videoCodec)),
        videoQualities: uniqueSorted(media.filter(item => item.kind === 'video').map(item => item.videoQuality)),
        audioCodecs: uniqueSorted(media.filter(item => item.kind === 'audio').map(item => item.audioCodec)),
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
