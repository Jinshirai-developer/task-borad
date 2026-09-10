import { mkdir, readFile, writeFile } from 'node:fs/promises';
const root = new URL('../../', import.meta.url);
const output = new URL('.local/cloudflare-pages/dist/', root);
const release = JSON.parse(await readFile(new URL('portfolio/release.json', root), 'utf8'));
const config = {
    distributionOrigin: 'https://taskboard-js-portfolio.jin-shirai-developer.chatgpt.site',
    distributionBase: release.base
};
await mkdir(output, { recursive: true });
const source = await readFile(new URL('./worker.mjs', import.meta.url), 'utf8');
await writeFile(new URL('_worker.js', output), source + '\nexport default createWorker(' + JSON.stringify(config) + ');\n');
await writeFile(new URL('robots.txt', output), 'User-agent: *\nDisallow: /\n');
await writeFile(new URL('_routes.json', output), JSON.stringify({ version: 1, include: ['/*'], exclude: [] }));
console.log('Cloudflare Pages bundle: .local/cloudflare-pages/dist (no credentials included)');
