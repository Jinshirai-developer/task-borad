import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createWorker } from './worker.mjs';
const publicOrigin = 'https://taskboard.example.test';
const config = { distributionOrigin: 'https://distribution.example.test', distributionBase: '/p/review-link/' };
function fixture(upstream = async () => new Response('ok')) {
    const calls = [];
    const worker = createWorker(config, async (url, options) => { calls.push({ url, options }); return upstream(url, options); });
    const env = { PUBLIC_ORIGIN: publicOrigin, MAC_SERVER_ORIGIN: 'https://mac.example.test', MAC_SERVER_PROXY_KEY: 'private-gateway-key' };
    const call = (path, options = {}, host = publicOrigin) => worker.fetch(new Request(host + path, options), env);
    return { call, calls, env };
}
test('only the configured production origin can use the gateway', async () => {
    const { call, calls, env } = fixture();
    for (const host of ['https://preview.taskboard.example.test', 'https://another.example.test']) assert.equal((await call('/api/auth/config', {}, host)).status, 404);
    for (const value of [undefined, 'http://taskboard.example.test', publicOrigin + '/extra', 'https://user:password@taskboard.example.test']) {
        env.PUBLIC_ORIGIN = value;
        assert.equal((await call('/login.html')).status, 404);
    }
    assert.equal(calls.length, 0);
});
test('root opens registration and all routes discourage indexing', async () => {
    const { call, calls } = fixture();
    const root = await call('/');
    assert.equal(root.status, 302);
    assert.equal(root.headers.get('Location'), '/login.html?mode=register');
    assert.equal(root.headers.get('Cache-Control'), 'no-store');
    assert.equal(root.headers.get('X-Robots-Tag'), 'noindex, nofollow, noarchive');
    assert.match(await (await call('/robots.txt')).text(), /Disallow: \//);
    for (const path of ['/.env', '/_worker.js', '/_release-assets/hash', '/download/unknown', '/objects/private']) assert.equal((await call(path)).status, 404);
    assert.equal(calls.length, 0);
});
test('API requests preserve CSRF and body while excluding unrelated credentials and forged headers', async () => {
    const { call, calls } = fixture();
    const result = await call('/api/tasks?workspace=team', { method: 'POST', headers: {
        Origin: publicOrigin, 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf',
        Cookie: '__Host-TaskBoard.Auth=chunks-2; __Host-TaskBoard.AuthC1=first; __Host-TaskBoard.AuthC2=second; __Host-TaskBoard.Csrf=csrf-cookie; platform-session=private; __Host-TaskBoard.Review=old',
        Authorization: 'Bearer must-not-forward', 'X-TaskBoard-Proxy-Key': 'forged', 'X-TaskBoard-Client-IP': 'forged',
        'CF-Connecting-IP': '203.0.113.7', 'X-Forwarded-Host': 'evil.example.test'
    }, body: '{"title":"保存の確認"}' });
    assert.equal(result.status, 200);
    assert.equal(calls[0].url, 'https://mac.example.test/api/tasks?workspace=team');
    const { headers, body, redirect } = calls[0].options;
    assert.equal(await new Response(body).text(), '{"title":"保存の確認"}');
    assert.equal(headers.get('X-CSRF-TOKEN'), 'csrf');
    assert.equal(headers.get('Origin'), publicOrigin);
    assert.equal(headers.get('X-TaskBoard-Proxy-Key'), 'private-gateway-key');
    assert.equal(headers.get('X-TaskBoard-Client-IP'), '203.0.113.7');
    assert.equal(headers.get('Authorization'), null);
    assert.equal(headers.get('X-Forwarded-Host'), null);
    assert.equal(headers.get('Cookie'), '__Host-TaskBoard.Auth=chunks-2; __Host-TaskBoard.AuthC1=first; __Host-TaskBoard.AuthC2=second; __Host-TaskBoard.Csrf=csrf-cookie');
    assert.equal(redirect, 'manual');
});
test('cross-origin writes and malformed Mac configuration fail before forwarding', async () => {
    const { call, calls, env } = fixture();
    for (const value of ['https://other.example.test', 'null']) assert.equal((await call('/api/tasks', { method: 'POST', headers: { Origin: value } })).status, 403);
    for (const value of ['http://mac.example.test', 'https://mac.example.test/path', 'https://mac.example.test?secret=value', 'invalid']) {
        env.MAC_SERVER_ORIGIN = value;
        assert.equal((await call('/api/auth/config')).status, 503);
    }
    assert.equal(calls.length, 0);
});
test('Google redirects, correlation and chunked auth cookies survive the proxy', async () => {
    const { call } = fixture(async () => {
        const headers = new Headers({ Location: 'https://accounts.google.com/o/oauth2/v2/auth?state=opaque', 'Content-Security-Policy': "connect-src 'self'" });
        for (const cookie of ['__Host-TaskBoard.Auth=chunks-2', '__Host-TaskBoard.AuthC1=first', '__Host-TaskBoard.AuthC2=second', '.AspNetCore.Correlation.abc=token', 'platform-session=private']) headers.append('Set-Cookie', cookie + '; Path=/; Secure; HttpOnly');
        return new Response(null, { status: 302, headers });
    });
    const response = await call('/api/auth/google/start');
    assert.equal(response.headers.get('Location'), 'https://accounts.google.com/o/oauth2/v2/auth?state=opaque');
    assert.equal(response.headers.getSetCookie().length, 4);
    assert.equal(response.headers.get('Content-Security-Policy'), "connect-src 'self'");
    assert.equal(response.headers.get('Cache-Control'), 'no-store');
});
test('login introduction points to the new local download page', async () => {
    const { call } = fixture(async () => new Response('<span class="login-project-label"><span aria-hidden="true"></span>ポートフォリオデモ</span>', { headers: { 'Content-Type': 'text/html', ETag: 'old', 'Content-Length': '123' } }));
    const response = await call('/login.html?mode=register');
    assert.match(await response.text(), /href="\/about">アプリ紹介・Windows版/);
    assert.equal(response.headers.get('ETag'), null);
    assert.equal(response.headers.get('Content-Length'), null);
});
test('download ranges stream unchanged and no app credentials reach the distribution service', async () => {
    const { call, calls } = fixture(async () => new Response('ZIP', { status: 206, headers: {
        'Content-Type': 'application/zip', 'Content-Range': 'bytes 0-2/197408027', 'Content-Length': '3',
        'Content-Disposition': 'attachment; filename="windows.zip"', 'Set-Cookie': 'platform-session=private'
    } }));
    const response = await call('/download/windows', { headers: { Range: 'bytes=0-2', Cookie: '__Host-TaskBoard.Auth=secret', Authorization: 'Bearer private', 'X-TaskBoard-Proxy-Key': 'private' } });
    assert.equal(response.status, 206);
    assert.equal(await response.text(), 'ZIP');
    assert.equal(response.headers.get('Content-Range'), 'bytes 0-2/197408027');
    assert.equal(response.headers.get('Content-Disposition'), 'attachment; filename="windows.zip"');
    assert.equal(response.headers.get('Set-Cookie'), null);
    assert.equal(calls[0].url, config.distributionOrigin + config.distributionBase + 'download/windows');
    assert.equal(calls[0].options.headers.get('Range'), 'bytes=0-2');
    for (const key of ['Cookie', 'Authorization', 'X-TaskBoard-Proxy-Key']) assert.equal(calls[0].options.headers.get(key), null);
    assert.equal(calls[0].options.signal, undefined);
});
test('distribution HTML and redirects remain on the short URL', async () => {
    const { call } = fixture(async url => {
        if (url.includes('demo/')) return new Response(null, { status: 302, headers: { Location: config.distributionBase + 'about#about-demo' } });
        return new Response('<a href="' + config.distributionBase + '">登録</a><a href="' + config.distributionOrigin + config.distributionBase + 'download/windows">DL</a>', { headers: { 'Content-Type': 'text/html' } });
    });
    assert.equal(await (await call('/about')).text(), '<a href="/">登録</a><a href="/download/windows">DL</a>');
    assert.equal((await call('/demo/terms.html')).headers.get('Location'), '/about#about-demo');
});
test('distribution rejects writes and unexpected external redirects', async () => {
    const { call, calls } = fixture(async () => new Response(null, { status: 302, headers: { Location: 'https://untrusted.example.test/' } }));
    assert.equal((await call('/download/windows', { method: 'POST', body: 'private' })).status, 405);
    assert.equal(calls.length, 0);
    assert.equal((await call('/about')).status, 502);
});
test('Mac outages do not prevent the independent demo and download', async () => {
    const { call } = fixture(async url => {
        if (url.startsWith('https://mac.example.test')) throw new Error('offline');
        return new Response('distribution available');
    });
    const api = await call('/api/auth/config');
    assert.equal(api.status, 503);
    assert.match((await api.json()).message, /接続できません/);
    const login = await call('/login.html');
    assert.equal(login.status, 503);
    assert.match(await login.text(), /href="\/demo\/index.html\?demo=1"/);
    assert.equal(await (await call('/download/windows')).text(), 'distribution available');
    assert.equal(await (await call('/demo/index.html?demo=1')).text(), 'distribution available');
});
