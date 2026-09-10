import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const project = path.dirname(fileURLToPath(import.meta.url));
const input = fs.existsSync(path.join(project, 'frontend')) ? project : path.dirname(project);
const release = JSON.parse(fs.readFileSync(path.join(project, 'release.json')));
const releaseZip = process.env.TASKBOARD_WINDOWS_ZIP || path.join(input, '.local/windows-0.1.0', release.name);
const dist = path.join(project, 'dist/server'), uploadRoot = path.join(project, 'uploads');
fs.mkdirSync(dist, { recursive: true }); fs.mkdirSync(uploadRoot, { recursive: true });
const textFiles = {}, objects = {}, uploadFiles = [];
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.png': 'image/png', '.svg': 'image/svg+xml', '.webp': 'image/webp' };
function add(file, buffer) {
    const type = types[path.extname(file)];
    if (!type) return;
    if (['.html', '.css', '.js', '.svg'].includes(path.extname(file))) {
        let content = buffer.toString();
        if (file === 'demo/index.html') content = content.replace('</head>', '<meta name="robots" content="noindex,nofollow">\n</head>')
            .replace(/<nav class="app-legal-links"[^>]*>.*?<\/nav>/, '<nav class="app-legal-links" aria-label="体験版のご案内"><a href="../about#about-demo">体験版について</a></nav>')
            .replace('</body>', '<script src="../demo-entry.js"></script>\n</body>');
        textFiles[file] = { content, type };
    } else addObject(file, buffer, type);
}
function addObject(file, bytes, type) {
    const sha256 = hash(bytes), key = 'objects/' + sha256;
    const filename = path.join(uploadRoot, sha256);
    fs.writeFileSync(filename, bytes);
    objects[file] = { key, sha256, size: bytes.length, type };
    if (!uploadFiles.some(item => item.sha256 === sha256)) uploadFiles.push({ ...objects[file], path: filename });
    return objects[file];
}
function walk(directory, prefix) {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
        if (['previews', 'sources', 'drafts', 'archive'].includes(item.name) || item.name.startsWith('.')) continue;
        const filename = path.join(directory, item.name), target = prefix + item.name;
        if (item.isSymbolicLink()) throw new Error('Symlink in public source');
        if (item.isDirectory()) walk(filename, target + '/');
        else if (!/^(login|auth|google|desktop)\.(html|js|css)$/.test(item.name)) add(target, fs.readFileSync(filename));
    }
}
walk(path.join(input, 'frontend'), 'demo/');
for (const filename of ['index.html', 'site.css', 'demo-entry.js']) add(filename, fs.readFileSync(path.join(project, filename)));
add('board.png', fs.readFileSync(path.join(input, 'docs/screenshots/app/v22/board-demo.png')));
const archive = fs.readFileSync(releaseZip);
if (hash(archive) !== release.sha256 || archive.length !== release.size) throw new Error('Windows release does not match verified package');
release.parts = [];
for (let offset = 0, part = 0; offset < archive.length; offset += 16 * 1024 * 1024, part++) {
    release.parts.push(addObject('_windows/' + part, archive.subarray(offset, Math.min(offset + 16 * 1024 * 1024, archive.length)), 'application/octet-stream'));
}
const manifestHash = hash(Buffer.from(JSON.stringify(objects)));
const ready = addObject('_ready', Buffer.from(JSON.stringify({ manifestHash, release: release.sha256 })), 'application/json');
const config = { base: release.base, release, readyKey: ready.key };
const source = fs.readFileSync(path.join(project, 'worker.mjs'), 'utf8');
fs.writeFileSync(path.join(dist, 'index.js'), source + '\nexport default createWorker(' + JSON.stringify(config) + ',' + JSON.stringify(textFiles) + ',' + JSON.stringify(objects) + ');\n');
fs.writeFileSync(path.join(uploadRoot, 'manifest.json'), JSON.stringify({ ...config, files: uploadFiles }, null, 2));
console.log(`Built ${Object.keys(textFiles).length} text resources and ${uploadFiles.length} verified file objects.`);
console.log(`Worker: ${fs.statSync(path.join(dist, 'index.js')).size} bytes. Download: ${release.size} bytes.`);
