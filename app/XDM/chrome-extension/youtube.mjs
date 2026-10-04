export function youtubeVideoUrl(value) {
    try {
        const url = new URL(value);
        if (!['http:', 'https:'].includes(url.protocol)) return null;
        let id;
        if (url.hostname === 'youtu.be') id = url.pathname.replace(/^\/+|\/+$/g, '');
        else if (url.hostname === 'youtube.com' || url.hostname.endsWith('.youtube.com')) {
            if (url.pathname === '/watch') id = url.searchParams.get('v');
            else id = /^\/(?:shorts|live|embed)\/([^/]+)\/?$/.exec(url.pathname)?.[1];
        }
        return /^[A-Za-z0-9_-]{11}$/.test(id || '') ? `https://www.youtube.com/watch?v=${id}` : null;
    } catch { return null; }
}
