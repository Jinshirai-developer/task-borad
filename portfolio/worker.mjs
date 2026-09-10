const safetyHeaders = {
    'X-Robots-Tag': 'noindex, nofollow, noarchive',
    'Referrer-Policy': 'no-referrer',
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
    'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'none'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'none'"
};
function reply(body, status = 200, headers = {}) {
    return new Response(body, { status, headers: { ...safetyHeaders, 'Cache-Control': 'no-store', ...headers } });
}
function json(data, status = 200) {
    return reply(JSON.stringify(data), status, { 'Content-Type': 'application/json; charset=utf-8' });
}
export function parseRange(header, size) {
    if (!header) return { start: 0, end: size - 1, partial: false };
    const match = /^bytes=(\d*)-(\d*)$/.exec(header);
    if (!match || (!match[1] && !match[2])) return null;
    let start, end;
    if (!match[1]) { const suffix = Number(match[2]); if (!Number.isSafeInteger(suffix) || suffix <= 0) return null; start = Math.max(0, size - suffix); end = size - 1; }
    else { start = Number(match[1]); end = match[2] ? Number(match[2]) : size - 1; }
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start < 0 || start >= size || end < start) return null;
    return { start, end: Math.min(end, size - 1), partial: true };
}
export function createWorker(config, textFiles, objects) {
    const byHash = new Map(Object.values(objects).map(object => [object.sha256, object]));
    async function authorized(request, env) {
        if (!env.RELEASE_UPLOAD_TOKEN || !env.RELEASE_UPLOAD_EXPIRES || Date.now() >= Number(env.RELEASE_UPLOAD_EXPIRES)) return false;
        const supplied = request.headers.get('Authorization') || '';
        if (supplied.length > 256) return false;
        const encoder = new TextEncoder();
        const a = new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(supplied)));
        const b = new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode('Bearer ' + env.RELEASE_UPLOAD_TOKEN)));
        return a.reduce((result, byte, index) => result | (byte ^ b[index]), 0) === 0;
    }
    async function ingest(request, env, pathname) {
        if (!await authorized(request, env)) return reply('Not found', 404);
        const object = byHash.get(pathname.slice('/_release-assets/'.length));
        if (!object) return reply('Not found', 404);
        if (request.method === 'HEAD') {
            const stored = await env.FILES.head(object.key);
            return reply(null, stored?.size === object.size && stored.customMetadata?.sha256 === object.sha256 ? 200 : 404);
        }
        if (request.method !== 'PUT') return reply('Method not allowed', 405, { Allow: 'PUT, HEAD' });
        if (request.headers.get('Content-Length') !== String(object.size)) return json({ error: 'Incorrect file size' }, 400);
        await env.FILES.put(object.key, request.body, {
            sha256: object.sha256,
            httpMetadata: { contentType: object.type },
            customMetadata: { sha256: object.sha256 }
        });
        return json({ size: object.size, sha256: object.sha256 });
    }
    async function download(request, env) {
        const range = parseRange(request.headers.get('Range'), config.release.size);
        if (!range) return reply(null, 416, { 'Content-Range': `bytes */${config.release.size}` });
        if (!await env.FILES.head(config.readyKey)) return reply('ダウンロードを準備しています。時間をおいて再度お試しください。', 503, { 'Retry-After': '60', 'Content-Type': 'text/plain; charset=utf-8' });
        const headers = {
            'Content-Type': 'application/zip',
            'Content-Disposition': `attachment; filename="${config.release.name}"`,
            'Content-Length': String(range.end - range.start + 1),
            'Accept-Ranges': 'bytes',
            ETag: `"${config.release.sha256}"`
        };
        if (range.partial) headers['Content-Range'] = `bytes ${range.start}-${range.end}/${config.release.size}`;
        if (request.method === 'HEAD') return reply(null, range.partial ? 206 : 200, headers);
        let offset = 0;
        const parts = config.release.parts.map(part => {
            const start = offset; offset += part.size;
            return { ...part, start, end: offset - 1 };
        }).filter(part => part.end >= range.start && part.start <= range.end);
        let index = 0, reader;
        const stream = new ReadableStream({
            async pull(controller) {
                try {
                    while (true) {
                        if (!reader) {
                            if (index >= parts.length) { controller.close(); return; }
                            const part = parts[index++], start = Math.max(range.start, part.start) - part.start;
                            const length = Math.min(range.end, part.end) - (part.start + start) + 1;
                            const object = await env.FILES.get(part.key, { range: { offset: start, length } });
                            if (!object?.body) throw new Error('Release part unavailable');
                            reader = object.body.getReader();
                        }
                        const result = await reader.read();
                        if (result.done) { reader.releaseLock(); reader = null; continue; }
                        controller.enqueue(result.value); return;
                    }
                } catch (error) { controller.error(error); }
            },
            async cancel(reason) { if (reader) await reader.cancel(reason); }
        });
        return reply(stream, range.partial ? 206 : 200, headers);
    }
    return {
        async fetch(request, env) {
            const url = new URL(request.url), pathname = url.pathname;
            if (pathname.startsWith('/_release-assets/')) return ingest(request, env, pathname);
            if (!['GET', 'HEAD'].includes(request.method)) return reply('Method not allowed', 405, { Allow: 'GET, HEAD' });
            if (pathname === '/robots.txt') return reply(request.method === 'HEAD' ? null : 'User-agent: *\nDisallow: /\n', 200, { 'Content-Type': 'text/plain' });
            if (pathname === config.base.slice(0, -1)) return reply(null, 302, { Location: config.base });
            if (!pathname.startsWith(config.base)) return reply('Not found', 404);
            const relative = pathname.slice(config.base.length);
            if (relative === 'download/windows') return download(request, env);
            if (relative === 'download/sha256') return reply(request.method === 'HEAD' ? null : `${config.release.sha256}  ${config.release.name}\n`, 200, { 'Content-Type': 'text/plain; charset=utf-8' });
            if (relative === 'demo/' || relative === 'demo/index.html') {
                if (url.searchParams.get('demo') !== '1') return reply(null, 302, { Location: config.base + 'demo/index.html?demo=1' });
            }
            if (/^demo\/(login|auth|google|desktop)\.html$/.test(relative)) return reply(null, 302, { Location: config.base });
            if (/^demo\/(terms|privacy)\.html$/.test(relative)) return reply(null, 302, { Location: config.base + '#about-demo' });
            const file = relative === '' ? 'index.html' : relative === 'demo/' ? 'demo/index.html' : relative;
            if (Object.hasOwn(textFiles, file)) {
                const entry = textFiles[file];
                return reply(request.method === 'HEAD' ? null : entry.content, 200, { 'Content-Type': entry.type });
            }
            if (Object.hasOwn(objects, file) && !file.startsWith('_')) {
                const entry = objects[file];
                if (request.method === 'HEAD') {
                    const object = await env.FILES.head(entry.key);
                    return reply(null, object ? 200 : 404, object ? { 'Content-Type': entry.type, 'Content-Length': String(entry.size) } : {});
                }
                const object = await env.FILES.get(entry.key);
                if (object) return reply(object.body, 200, { 'Content-Type': entry.type, 'Content-Length': String(entry.size), ETag: `"${entry.sha256}"`, 'Cache-Control': 'private, max-age=86400' });
            }
            return reply('Not found', 404);
        }
    };
}
