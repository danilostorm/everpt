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
        // The opening scene constructs its skies, effects and audio resources
        // synchronously. They must be fetched in parallel beforehand: deferring
        // almost all startup assets makes initial game loading stall for minutes.
        return response.ok ? await response.json() : { startup: [], world: [] };
    } catch {
        return { startup: [], world: [] };
    }
}

export function preloadStartup(list, onProgress) {
    return preloadCompactBundle(list, onProgress);
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

 
// Content-addressed startup packs reduce thousands of HTTP requests to one
// compressed response. Never assume a pack exists: old deployments and browsers
// without streaming gzip always use the original per-file fetch path.
async function preloadCompactBundle(list, onProgress) {
    const fallback = () => {
        for (const path of list) prefetched.delete(path);
        return fetchAll(list, true, PreloadConcurrency, onProgress);
    };
    if (!('DecompressionStream' in window) || !globalThis.crypto?.subtle
        || (navigator.deviceMemory && navigator.deviceMemory < 4)) {
        return fallback();
    }
    try {
        const metadataResponse = await fetch('startup-pack.json');
        if (!metadataResponse.ok) return fallback();
        const metadata = await metadataResponse.json();
        if (metadata.format !== 'EVPT1-gzip' || metadata.count !== list.length
            || !Number.isSafeInteger(metadata.compressedBytes)
            || metadata.compressedBytes <= 0
            || !/^startup-[a-f0-9]{16}\\.pack$/.test(metadata.url)) {
            throw new Error('Incompatible startup pack manifest');
        }
        const response = await fetch(metadata.url);
        if (!response.ok || !response.body) throw new Error('Startup pack unavailable');
        const reader = response.body.getReader();
        const chunks = [];
        let received = 0;
        while (true) {
            const {done, value} = await reader.read();
            if (done) break;
            chunks.push(value);
            received += value.byteLength;
            if (received > metadata.compressedBytes)
                throw new Error('Oversized startup pack');
            const fraction = received / metadata.compressedBytes;
            onProgress(Math.floor(fraction * metadata.uncompressedBytes),
                metadata.uncompressedBytes, Math.floor(fraction * list.length), list.length);
        }
        if (received !== metadata.compressedBytes)
            throw new Error('Truncated startup pack');
        const compressed = new Uint8Array(received);
        let copied = 0;
        for (const chunk of chunks) {
            compressed.set(chunk, copied);
            copied += chunk.byteLength;
        }
        const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', compressed)))
            .map(b => b.toString(16).padStart(2, '0')).join('');
        if (hash !== metadata.sha256)
            throw new Error('Startup pack integrity check failed');
        const uncompressed = new Uint8Array(await new Response(
            new Blob([compressed]).stream().pipeThrough(new DecompressionStream('gzip'))).arrayBuffer());
        const view = new DataView(uncompressed.buffer);
        const decoder = new TextDecoder('utf-8');
        if (decoder.decode(uncompressed.subarray(0, 5)) !== 'EVPT1')
            throw new Error('Unrecognized startup pack');
        let offset = 5;
        const count = view.getUint32(offset, true); offset += 4;
        if (count !== list.length) throw new Error('Wrong startup pack file count');
        const required = new Set(list);
        let totalBytes = 0;
        for (let i = 0; i < count; i++) {
            const nameLength = view.getUint16(offset, true); offset += 2;
            const size = view.getUint32(offset, true); offset += 4;
            const path = decoder.decode(uncompressed.subarray(offset, offset + nameLength));
            offset += nameLength;
            if (!required.delete(path) || files.get(path)?.size !== size
                || offset + size > uncompressed.length) {
                throw new Error('Startup pack asset mismatch: ' + path);
            }
            prefetched.set(path, uncompressed.subarray(offset, offset + size));
            offset += size;
            totalBytes += size;
        }
        if (required.size || totalBytes !== metadata.uncompressedBytes
            || offset !== uncompressed.length) {
            throw new Error('Incomplete startup pack');
        }
        onProgress(totalBytes, totalBytes, count, count);
        console.info('[Web] Startup pack verified: ' + count + ' files');
    } catch (error) {
        console.warn('[Web] Startup pack unavailable, using original asset downloads: ' + error.message);
        return fallback();
    }
}
