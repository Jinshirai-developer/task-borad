const noindex = 'noindex, nofollow, noarchive';
const appCookie = value => /^(?:__Host-TaskBoard\.(?:Auth|Csrf|External)(?:C[1-9]\d*)?|\.AspNetCore\.Correlation\.[A-Za-z0-9_.-]+)=/.test(value.trim());
const safeMethod = method => ['GET', 'HEAD'].includes(method);
function origin(value) {
    try {
        const url = new URL(value);
        if (url.protocol === 'https:' && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash) return url.origin;
    } catch { /* Invalid configuration must not open the gateway. */ }
    return null;
}
function reply(body, status = 200, headers = {}) {
    return new Response(body, { status, headers: {
        'Cache-Control': 'no-store', 'X-Robots-Tag': noindex, 'Referrer-Policy': 'no-referrer',
        'X-Content-Type-Options': 'nosniff', 'X-Frame-Options': 'DENY',
        'Content-Security-Policy': "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
        ...headers
    } });
}
function appPath(path) {
    return ['/signin-google', '/health', '/health/ready'].includes(path)
        || path.startsWith('/api/')
        || /^\/[a-z0-9][a-z0-9.-]*\.(html|css|js)$/.test(path)
        || /^\/assets\/[A-Za-z0-9/_.,-]+\.(png|webp|svg)$/.test(path);
}
function distributionPath(path) {
    return ['/about', '/about/', '/site.css', '/demo-entry.js', '/board.png', '/download/windows', '/download/sha256'].includes(path)
        || path === '/demo/'
        || /^\/demo\/[A-Za-z0-9/_.,-]+\.(html|css|js|png|webp|svg)$/.test(path);
}
function responseHeaders(upstream, allowAppCookies) {
    const headers = new Headers(upstream.headers);
    headers.delete('Set-Cookie');
    if (allowAppCookies) for (const cookie of upstream.headers.getSetCookie()) {
        if (appCookie(cookie)) headers.append('Set-Cookie', cookie);
    }
    for (const key of ['Server', 'CF-Ray', 'Report-To', 'NEL', 'Alt-Svc']) headers.delete(key);
    headers.set('X-Robots-Tag', noindex);
    headers.set('Referrer-Policy', 'no-referrer');
    headers.set('Cache-Control', 'no-store');
    return headers;
}
function rewrittenBody(headers) {
    for (const key of ['Content-Length', 'Content-Encoding', 'ETag']) headers.delete(key);
}
function unavailable(request, url) {
    if (url.pathname.startsWith('/api/')) return reply(JSON.stringify({ message: 'サーバーに接続できません。時間をおいて再度お試しください。' }), 503, { 'Content-Type': 'application/json; charset=utf-8' });
    return reply(request.method === 'HEAD' ? null : '<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Task Board</title><h1>ただいま接続できません</h1><p>時間をおいて、もう一度お試しください。</p><p><a href="/demo/index.html?demo=1">登録不要の体験版を開く</a></p><p><a href="/about">アプリ紹介・Windows版</a></p></html>', 503, { 'Content-Type': 'text/html; charset=utf-8', 'Retry-After': '60' });
}

// Distribution stays on the existing Sites/R2 project. Credentials are never sent there.
export function createWorker(config, upstreamFetch = fetch) {
    const distributionOrigin = origin(config.distributionOrigin);
    const distributionBase = config.distributionBase;
    if (!distributionOrigin || !/^\/p\/[a-zA-Z0-9-]+\/$/.test(distributionBase)) throw new Error('Invalid distribution configuration');
    async function distribution(request, url) {
        if (!safeMethod(request.method)) return reply('Method not allowed', 405, { Allow: 'GET, HEAD' });
        const target = new URL(distributionBase + url.pathname.slice(1), distributionOrigin);
        target.search = url.search;
        const headers = new Headers();
        for (const key of ['Accept', 'Range', 'If-Range']) if (request.headers.has(key)) headers.set(key, request.headers.get(key));
        try {
            // Keep ZIP responses streaming; do not apply a short application timeout to downloads.
            const upstream = await upstreamFetch(target.href, { method: request.method, headers, redirect: 'manual' });
            const outputHeaders = responseHeaders(upstream, false);
            if (outputHeaders.has('Location')) {
                const destination = new URL(outputHeaders.get('Location'), target);
                if (destination.origin !== distributionOrigin) return reply('Invalid redirect', 502);
                if (destination.pathname.startsWith(distributionBase)) destination.pathname = '/' + destination.pathname.slice(distributionBase.length);
                outputHeaders.set('Location', destination.pathname + destination.search + destination.hash);
            }
            let body = upstream.body;
            if (request.method === 'GET' && outputHeaders.get('Content-Type')?.includes('text/html')) {
                body = (await upstream.text()).replaceAll(distributionOrigin + distributionBase, '/').replaceAll(distributionBase, '/');
                rewrittenBody(outputHeaders);
            }
            return new Response(body, { status: upstream.status, headers: outputHeaders });
        } catch { return unavailable(request, url); }
    }
    async function application(request, env, url) {
        const macOrigin = origin(env.MAC_SERVER_ORIGIN);
        if (!macOrigin || !env.MAC_SERVER_PROXY_KEY) return unavailable(request, url);
        if (!['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) return reply('Method not allowed', 405);
        if (!safeMethod(request.method) && request.headers.has('Origin') && request.headers.get('Origin') !== url.origin) return reply('Forbidden', 403);
        const target = new URL(url.pathname + url.search, macOrigin);
        const headers = new Headers();
        for (const key of ['Accept', 'Content-Type', 'X-CSRF-TOKEN', 'Origin', 'User-Agent']) if (request.headers.has(key)) headers.set(key, request.headers.get(key));
        const cookies = (request.headers.get('Cookie') || '').split(';').map(value => value.trim()).filter(appCookie);
        if (cookies.length) headers.set('Cookie', cookies.join('; '));
        headers.set('X-TaskBoard-Proxy-Key', env.MAC_SERVER_PROXY_KEY);
        headers.set('X-TaskBoard-Client-IP', request.headers.get('CF-Connecting-IP') || '0.0.0.0');
        try {
            const upstream = await upstreamFetch(target.href, {
                method: request.method, headers, redirect: 'manual', signal: AbortSignal.timeout(20_000),
                ...(!safeMethod(request.method) ? { body: request.body, duplex: 'half' } : {})
            });
            if (upstream.status >= 502) return unavailable(request, url);
            const outputHeaders = responseHeaders(upstream, true);
            let body = upstream.body;
            if (url.pathname === '/login.html' && request.method === 'GET' && upstream.status === 200) {
                body = (await upstream.text()).replace('<span class="login-project-label"><span aria-hidden="true"></span>ポートフォリオデモ</span>', '<a class="login-project-label" href="/about">アプリ紹介・Windows版</a>');
                rewrittenBody(outputHeaders);
            }
            return new Response(body, { status: upstream.status, headers: outputHeaders });
        } catch { return unavailable(request, url); }
    }
    return {
        async fetch(request, env) {
            const url = new URL(request.url);
            // Preview deployment hostnames must not receive production app credentials or cookies.
            if (origin(env.PUBLIC_ORIGIN) !== url.origin) return reply('Not found', 404);
            if (url.pathname === '/robots.txt' && safeMethod(request.method)) return reply(request.method === 'HEAD' ? null : 'User-agent: *\nDisallow: /\n', 200, { 'Content-Type': 'text/plain; charset=utf-8' });
            if (url.pathname === '/') return safeMethod(request.method) ? reply(null, 302, { Location: '/login.html?mode=register' }) : reply('Method not allowed', 405);
            if (distributionPath(url.pathname)) return distribution(request, url);
            if (appPath(url.pathname)) return application(request, env, url);
            return reply('Not found', 404);
        }
    };
}
