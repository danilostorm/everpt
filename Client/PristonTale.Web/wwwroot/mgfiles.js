// The game reads its data with ordinary file calls, so the data appears as a file tree: every file in the manifest exists
// with its real size. preload.json names the files the start needs, downloaded first with visible progress, and those the
// first world needs, downloaded in the background into the browser's cache. Anything else downloads when first read, which
// blocks the page while it lasts: the file system runs on the page's thread. See monogame/web/README.md.

const PreloadConcurrency = 8;
const BackgroundConcurrency = 3;

const stats = { files: 0, loaded: 0, loadedBytes: 0, loadMs: 0, slowest: 0, onDemand: 0 };
const files = new Map();
const prefetched = new Map();
export const loadLog = [];

export function fileStats() {
    return { ...stats };
}

function download(url) {
    const request = new XMLHttpRequest();
    request.open('GET', url, false);
    request.overrideMimeType('text/plain; charset=x-user-defined');
    request.send(null);
    if (request.status !== 200) {
        throw new Error(`${url}: HTTP ${request.status}`);
    }
    const text = request.responseText;
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i++) {
        bytes[i] = text.charCodeAt(i);
    }
    return bytes;
}

function mountFile(FS, path, folder, name, size) {
    const node = FS.createFile(folder, name, {}, true, false);
    let contents = null;
    const load = () => {
        if (contents === null) {
            const started = performance.now();
            contents = prefetched.get(path) || null;
            if (contents) {
                prefetched.delete(path);
            } else {
                contents = download(files.get(path).url);
                stats.onDemand++;
            }
            const elapsed = performance.now() - started;
            stats.loaded++;
            stats.loadedBytes += contents.length;
            stats.loadMs += elapsed;
            stats.slowest = Math.max(stats.slowest, elapsed);
            loadLog.push(path);
        }
        return contents;
    };
    Object.defineProperties(node, { usedBytes: { get: () => size } });
    const operations = {};
    for (const key of Object.keys(node.stream_ops)) {
        operations[key] = node.stream_ops[key];
    }
    operations.read = (stream, buffer, offset, length, position) => {
        const bytes = load();
        if (position >= bytes.length) {
            return 0;
        }
        const count = Math.min(bytes.length - position, length);
        buffer.set(bytes.subarray(position, position + count), offset);
        // Read to its end: the game has its own copy now. Reading it again finds it in the browser's cache.
        if (position + count >= bytes.length) {
            contents = null;
        }
        return count;
    };
    node.stream_ops = operations;
    stats.files++;
}

// Downloads in parallel, so the page keeps drawing. keep: hold the bytes for the game's first read.
async function fetchAll(list, keep, concurrency, onProgress) {
    let next = 0;
    let doneBytes = 0;
    let doneFiles = 0;
    const totalBytes = list.reduce((sum, path) => sum + (files.get(path)?.size || 0), 0);
    const worker = async () => {
        while (next < list.length) {
            const path = list[next++];
            const file = files.get(path);
            if (!file) {
                continue;
            }
            const response = await fetch(file.url);
            if (!response.ok) {
                throw new Error(`${file.url}: HTTP ${response.status}`);
            }
            const bytes = await response.arrayBuffer();
            if (keep) {
                prefetched.set(path, new Uint8Array(bytes));
            }
            doneBytes += bytes.byteLength;
            doneFiles++;
            onProgress(doneBytes, totalBytes, doneFiles, list.length);
        }
    };
    await Promise.all(Array.from({ length: concurrency }, worker));
}

export async function readManifest() {
    const manifest = await (await fetch('files.json')).json();
    for (const [mount, url, entries] of manifest.trees) {
        for (const [relative, size] of entries) {
            files.set(`${mount}/${relative}`, { size, url: `${url}/${relative.split('/').map(encodeURIComponent).join('/')}` });
        }
    }
    try {
        const response = await fetch('preload.json');
        if (!response.ok) return { startup: [], world: [] };
        const lists = await response.json();
        // Do not block the login screen on hundreds of world effects and music files.
        // The remaining files are prefetched after the first rendered frame, while
        // any needed earlier are still available on demand from the manifest.
        const critical = path => path.startsWith('/Content/')
            || path.startsWith('/assets/Hud/')
            || path.startsWith('/assets/image/Sinimage/Cursor/');
        return {
            startup: lists.startup.filter(critical),
            world: [...new Set([...lists.startup.filter(path => !critical(path)), ...lists.world])],
        };
    } catch {
        return { startup: [], world: [] };
    }
}

export function preloadStartup(list, onProgress) {
    return fetchAll(list, true, PreloadConcurrency, onProgress);
}

// Fills the browser's cache only, so the game's later reads return at once without holding the data here too.
export function preloadInBackground(list, onProgress) {
    return fetchAll(list, false, BackgroundConcurrency, onProgress);
}

export function mountFiles(FS, log) {
    const started = performance.now();
    const folders = new Set();
    for (const [path, file] of files) {
        const cut = path.lastIndexOf('/');
        const folder = path.slice(0, cut);
        if (!folders.has(folder)) {
            FS.createPath('/', folder.slice(1), true, true);
            folders.add(folder);
        }
        mountFile(FS, path, folder, path.slice(cut + 1), file.size);
    }
    log(`[Web] ${stats.files} files mounted in ${(performance.now() - started).toFixed(0)} ms.`);
}
