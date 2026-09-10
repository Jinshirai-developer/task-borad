import http from 'node:http';
import fs from 'node:fs';
import worker from './dist/server/index.js';
const manifest = JSON.parse(fs.readFileSync(new URL('./uploads/manifest.json', import.meta.url)));
const files = new Map(manifest.files.map(item => [item.key, item]));
const env = { FILES: {
    async head(key) { const entry = files.get(key); return entry ? { size: entry.size, customMetadata: {sha256:entry.sha256} } : null; },
    async get(key, options) { const entry = files.get(key); if (!entry) return null; let bytes = fs.readFileSync(entry.path); if (options?.range) bytes = bytes.subarray(options.range.offset, options.range.offset + options.range.length); return { body: new Response(bytes).body }; }
} };
http.createServer(async (req, res) => {
    try {
        const response = await worker.fetch(new Request('http://localhost:5117' + req.url, { method: req.method, headers: req.headers }), env);
        res.writeHead(response.status, Object.fromEntries(response.headers));
        if (response.body) for await (const bytes of response.body) res.write(bytes);
        res.end();
    } catch { res.writeHead(500); res.end('Preview failed'); }
}).listen(5117, '0.0.0.0', () => console.log('Local: http://localhost:5117' + manifest.base));
