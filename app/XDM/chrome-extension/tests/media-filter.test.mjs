import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

import {
    availableFilterOptions,
    filterMedia,
    formatMediaDetails,
    matchesMediaFilters,
    normalizeMedia
} from '../media-filter.mjs';

test('normalizes structured and fallback media metadata', () => {
    const structured = normalizeMedia({
        text: 'sample.mp4',
        info: 'ignored',
        extension: 'MP4',
        quality: '[MP4] 1080p 320 Kbps',
        size: 25 * 1024 * 1024
    });
    assert.equal(structured.extension, 'mp4');
    assert.equal(structured.videoQuality, '1080');
    assert.equal(structured.audioQuality, '320');
    assert.equal(structured.kind, 'video');
    assert.equal(structured.size, 25 * 1024 * 1024);

    const fallback = normalizeMedia({ text: 'track.mp3', info: '[MP3 AUDIO] 12.5 MiB 320 Kbps' });
    assert.equal(fallback.extension, 'mp3');
    assert.equal(fallback.audioQuality, '320');
    assert.equal(fallback.kind, 'audio');
    assert.equal(fallback.size, Math.round(12.5 * 1024 * 1024));
});

test('combines audio and video quality by media type', () => {
    const items = [
        { id: 'mp3-320', text: 'track.mp3', quality: '[MP3 AUDIO] 320 Kbps', size: 20 * 1024 * 1024 },
        { id: 'mp3-128', text: 'track.mp3', quality: '[MP3 AUDIO] 128 Kbps', size: 20 * 1024 * 1024 },
        { id: 'mp4-1080', text: 'movie.mp4', quality: '[MP4] 1080p', size: 40 * 1024 * 1024 },
        { id: 'mp4-720', text: 'movie.mp4', quality: '[MP4] 720p', size: 40 * 1024 * 1024 }
    ];
    const result = filterMedia(items, {
        extensions: ['mp3', 'mp4'],
        videoQuality: '1080',
        audioQuality: '320',
        minimumSizeMb: 0
    });
    assert.deepEqual(result.map(item => item.id), ['mp3-320', 'mp4-1080']);
});

test('minimum size is strictly greater and rejects unknown sizes', () => {
    const filters = { minimumSizeMb: 10 };
    assert.equal(matchesMediaFilters(normalizeMedia({ size: 11 * 1024 * 1024 }), filters), true);
    assert.equal(matchesMediaFilters(normalizeMedia({ size: 10 * 1024 * 1024 }), filters), false);
    assert.equal(matchesMediaFilters(normalizeMedia({ size: 0 }), filters), false);
});

test('recognizes the full 240p through 8K range when reported', () => {
    assert.equal(normalizeMedia({ quality: '240p' }).videoQuality, '240');
    assert.equal(normalizeMedia({ quality: '4320p' }).videoQuality, '4320');
    assert.equal(normalizeMedia({ extension: 'ts', quality: 'TS 1920x1080 6075 Kbps 1080' }).videoQuality, '1080');
    assert.equal(normalizeMedia({ extension: 'mp4', quality: '1920x1080' }).videoQuality, '1080');
});

test('builds extension and quality filters only from detected media', () => {
    const options = availableFilterOptions([
        { text: 'clip.webm', quality: '[WEBM] 720p' },
        { text: 'movie.mp4', quality: '[MP4] 1080p' },
        { text: 'track.opus', quality: '[OPUS AUDIO] 160 Kbps' },
        { text: 'song.mp3', quality: '[MP3 AUDIO] 320 Kbps' }
    ]);

    assert.deepEqual(options.extensions, ['mp3', 'mp4', 'opus', 'webm']);
    assert.deepEqual(options.videoQualities, ['720', '1080']);
    assert.deepEqual(options.audioQualities, ['160', '320']);
});

test('popup owns one fixed viewport and only the media list scrolls', async () => {
    const styles = await readFile(new URL('../styles.css', import.meta.url), 'utf8');

    assert.match(styles, /html\s*{[^}]*height:\s*600px;[^}]*overflow:\s*hidden;/s);
    assert.match(styles, /body\s*{[^}]*height:\s*600px;[^}]*overflow:\s*hidden;/s);
    assert.match(styles, /main\s*{[^}]*flex:\s*1 1 auto;[^}]*overflow-y:\s*auto;/s);
    assert.doesNotMatch(styles, /main\s*{[^}]*max-height:/s);
});

test('shows a numeric media size only once', () => {
    assert.equal(formatMediaDetails({ info: '[MP4] 1,1M', size: 1158569 }), '[MP4] · 1.1 MiB');
    assert.equal(formatMediaDetails({ info: '[MP4] 1920x1080 6075 Kbps', size: 1158569 }),
        '[MP4] 1920x1080 6075 Kbps · 1.1 MiB');
});
