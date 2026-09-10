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
    const reviewCookieName = '__Host-TaskBoard.Review';
    const reviewToken = encodeURIComponent(config.base);
    const reviewCookie = `${reviewCookieName}=${reviewToken}; Path=/; Secure; HttpOnly; SameSite=Lax; Max-Age=604800`;
    function reviewAccess(request) {
        const values = (request.headers.get('Cookie') || '').split(';').map(item => item.trim())
            .filter(item => item.startsWith(reviewCookieName + '='));
        return values.length === 1 && values[0] === reviewCookieName + '=' + reviewToken;
    }
    function appCookie(cookie) {
        return /^(?:__Host-TaskBoard\.(?:Auth|Csrf|External)(?:C[1-9]\d*)?|\.AspNetCore\.Correlation\.[A-Za-z0-9_.-]+)=/.test(cookie.trim());
    }
    function appPath(pathname) {
        return ['/', '/signin-google', '/health', '/health/ready'].includes(pathname)
            || pathname.startsWith('/api/')
            || /^\/[a-z0-9][a-z0-9.-]*\.(html|css|js)$/.test(pathname)
            || /^\/assets\/[A-Za-z0-9/_.,-]+\.(png|webp|svg)$/.test(pathname);
    }
    async function proxy(request, env, url) {
        const origin = new URL(env.MAC_SERVER_ORIGIN);
        if (origin.protocol !== 'https:' || origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash)
            return reply('Server configuration unavailable', 503);
        if (!['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) return reply('Method not allowed', 405);
        if (!['GET', 'HEAD'].includes(request.method) && request.headers.has('Origin') && request.headers.get('Origin') !== url.origin)
            return reply('Forbidden', 403);
        const headers = new Headers();
        for (const key of ['Accept', 'Content-Type', 'X-CSRF-TOKEN', 'Origin', 'User-Agent'])
            if (request.headers.has(key)) headers.set(key, request.headers.get(key));
        const cookies = (request.headers.get('Cookie') || '').split(';').map(item => item.trim()).filter(appCookie);
        if (cookies.length) headers.set('Cookie', cookies.join('; '));
        headers.set('X-TaskBoard-Proxy-Key', env.MAC_SERVER_PROXY_KEY);
        headers.set('X-TaskBoard-Client-IP', request.headers.get('CF-Connecting-IP') || '0.0.0.0');
        origin.pathname = url.pathname === '/' ? '/index.html' : url.pathname;
        origin.search = url.search;
        try {
            const upstream = await fetch(origin.href, {
                method: request.method, headers, redirect: 'manual', signal: AbortSignal.timeout(20_000),
                ...(!['GET', 'HEAD'].includes(request.method) ? { body: request.body, duplex: 'half' } : {})
            });
            if (upstream.status >= 502) throw new Error('Mac unavailable');
            const responseHeaders = new Headers(upstream.headers);
            responseHeaders.delete('Set-Cookie');
            for (const cookie of upstream.headers.getSetCookie()) if (appCookie(cookie)) responseHeaders.append('Set-Cookie', cookie);
            for (const key of ['Server', 'CF-Ray', 'Report-To', 'NEL', 'Alt-Svc']) responseHeaders.delete(key);
            responseHeaders.set('X-Robots-Tag', safetyHeaders['X-Robots-Tag']);
            responseHeaders.set('Referrer-Policy', 'no-referrer');
            responseHeaders.set('Cache-Control', 'no-store');
            let body = upstream.body;
            if (url.pathname === '/login.html' && request.method === 'GET' && upstream.status === 200) {
                body = (await upstream.text()).replace('<span class="login-project-label"><span aria-hidden="true"></span>ポートフォリオデモ</span>',
                    `<a class="login-project-label" href="${config.base}about">アプリ紹介・Windows版</a>`);
                responseHeaders.delete('Content-Length');
                responseHeaders.delete('Content-Encoding');
                responseHeaders.delete('ETag');
            }
            return new Response(body, { status: upstream.status, headers: responseHeaders });
        } catch {
            if (url.pathname.startsWith('/api/')) return json({ message: 'サーバーに接続できません。時間をおいて再度お試しください。' }, 503);
            return reply(`<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Task Board</title><h1>ただいま接続できません</h1><p>時間をおいて、もう一度お試しください。</p><p><a href="${config.base}demo/index.html?demo=1">登録不要の体験版を開く</a></p><p><a href="${config.base}about">アプリ紹介・Windows版</a></p></html>`, 503, { 'Content-Type': 'text/html; charset=utf-8', 'Retry-After': '60' });
        }
    }
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
            const serverEnabled = Boolean(env.MAC_SERVER_ORIGIN && env.MAC_SERVER_PROXY_KEY);
            if (serverEnabled && appPath(pathname) && !pathname.startsWith(config.base)) {
                if (!reviewAccess(request)) return reply('Not found', 404);
                return proxy(request, env, url);
            }
            if (!['GET', 'HEAD'].includes(request.method)) return reply('Method not allowed', 405, { Allow: 'GET, HEAD' });
            if (pathname === '/robots.txt') return reply(request.method === 'HEAD' ? null : 'User-agent: *\nDisallow: /\n', 200, { 'Content-Type': 'text/plain' });
            if (pathname === config.base.slice(0, -1)) return reply(null, 302, { Location: config.base });
            if (!pathname.startsWith(config.base)) return reply('Not found', 404);
            const relative = pathname.slice(config.base.length);
            if (serverEnabled && (relative === '' || /^(login|auth|google|index|desktop)\.html$/.test(relative))) {
                const entry = relative === '' ? '/login.html?mode=register' : '/' + relative + url.search;
                return reply(null, 302, { Location: entry, 'Set-Cookie': reviewCookie });
            }
            if (relative === 'about/') return reply(null, 302, { Location: config.base + 'about' });
            if (relative === 'download/windows') return download(request, env);
            if (relative === 'download/sha256') return reply(request.method === 'HEAD' ? null : `${config.release.sha256}  ${config.release.name}\n`, 200, { 'Content-Type': 'text/plain; charset=utf-8' });
            if (relative === 'demo/' || relative === 'demo/index.html') {
                if (url.searchParams.get('demo') !== '1') return reply(null, 302, { Location: config.base + 'demo/index.html?demo=1' });
            }
            if (/^demo\/(login|auth|google|desktop)\.html$/.test(relative)) return reply(null, 302, { Location: config.base });
            if (/^demo\/(terms|privacy)\.html$/.test(relative)) return reply(null, 302, { Location: config.base + 'about#about-demo' });
            const file = relative === '' || relative === 'about' ? 'index.html' : relative === 'demo/' ? 'demo/index.html' : relative;
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
