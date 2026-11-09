// The game reads its data with ordinary file calls, so the data appears as a file tree: every file in the manifest exists
// with its real size. preload.json names the files the start needs, downloaded first with visible progress, and those the
// first world needs, downloaded in the background into the browser's cache. Anything else downloads when first read, which
// blocks the page while it lasts: the file system runs on the page's thread. See monogame/web/README.md.

const PreloadConcurrency = 8;
const BackgroundConcurrency = 3;

const stats = { files: 0, loaded: 0, loadedBytes: 0, loadMs: 0, slowest: 0, onDemand: 0, cacheHits: 0 };
const files = new Map();
const prefetched = new Map();
const readCache = new Map();
const MaxReadCacheBytes = 384 * 1024 * 1024;
let readCacheBytes = 0;
export const loadLog = [];

// The opening scene reopens many assets. Re-fetching them synchronously on
// the game thread can stall the public browser for minutes. Retain a bounded
// in-memory LRU to reuse bytes across file handles and repeated reads.
function cachedFile(path) {
    const bytes = readCache.get(path);
    if (!bytes) return null;
    readCache.delete(path);
    readCache.set(path, bytes);
    return bytes;
}
function keepFile(path, bytes) {
    if (bytes.byteLength > MaxReadCacheBytes) return;
    const old = readCache.get(path);
    if (old) {
        readCacheBytes -= old.byteLength;
        readCache.delete(path);
    }
    readCache.set(path, bytes);
    readCacheBytes += bytes.byteLength;
    while (readCacheBytes > MaxReadCacheBytes) {
        const [oldest, oldestBytes] = readCache.entries().next().value;
        readCache.delete(oldest);
        readCacheBytes -= oldestBytes.byteLength;
    }
}

export function fileStats() {
    return { ...stats, readCacheBytes };
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
                keepFile(path, contents);
            } else {
                contents = cachedFile(path);
                if (contents) {
                    stats.cacheHits++;
                } else {
                    contents = download(files.get(path).url);
                    stats.onDemand++;
                    keepFile(path, contents);
                }
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
        if (position >= size) return 0;
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
            || !Number.isSafeInteger(metadata.uncompressedBytes)
            || metadata.compressedBytes <= 0
            || metadata.compressedBytes > 512 * 1024 * 1024
            || metadata.uncompressedBytes <= 0
            || metadata.uncompressedBytes > 1024 * 1024 * 1024
            || !/^startup-[a-f0-9]{16}\.pack$/.test(metadata.url)) {
            throw new Error('Incompatible startup pack manifest');
        }
        const compressed = await loadVerifiedStartupPack(metadata, onProgress, list.length);
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

// Large WebPT bundles arrive much faster over several independent CDN byte
// ranges. A strict Content-Range check prevents concatenating mismatched data.
// Always verify the final SHA-256 before attempting to decompress it.
async function downloadCompressedPack(meta, onProgress, count) {
    try {
        return await downloadParallelRanges(meta, onProgress, count);
    } catch (error) {
        console.info('[Web] Parallel pack unavailable; using a single request: ' + error.message);
        return downloadSinglePack(meta, onProgress, count);
    }
}

async function downloadParallelRanges(meta, onProgress, count) {
    const size = meta.compressedBytes;
    if (size < 4 * 1024 * 1024) throw new Error('Pack too small for byte ranges');
    const probe = await fetch(meta.url, { headers: { Range: 'bytes=0-0' } });
    if (probe.status !== 206 || probe.headers.get('content-range') !== `bytes 0-0/${size}`) {
        await probe.body?.cancel();
        throw new Error('Server does not support precise byte ranges');
    }
    if ((await probe.arrayBuffer()).byteLength !== 1)
        throw new Error('Invalid range probe');

    const step = 8 * 1024 * 1024;
    const segments = [];
    for (let start = 0; start < size; start += step)
        segments.push({ start, end: Math.min(size - 1, start + step - 1) });
    const packed = new Uint8Array(size);
    const cancel = new AbortController();
    let next = 0, complete = 0;

    const worker = async () => {
        while (next < segments.length) {
            const { start, end } = segments[next++];
            let done = false;
            for (let retry = 0; retry < 2 && !done; retry++) {
                try {
                    const response = await fetch(meta.url, {
                        headers: { Range: `bytes=${start}-${end}` },
                        signal: cancel.signal,
                    });
                    if (response.status !== 206 ||
                        response.headers.get('content-range') !== `bytes ${start}-${end}/${size}`) {
                        await response.body?.cancel();
                        throw new Error('Unexpected Content-Range for ' + start);
                    }
                    const bytes = new Uint8Array(await response.arrayBuffer());
                    if (bytes.byteLength !== end - start + 1)
                        throw new Error('Truncated range at ' + start);
                    packed.set(bytes, start);
                    complete += bytes.byteLength;
                    const fraction = complete / size;
                    onProgress(Math.floor(fraction * meta.uncompressedBytes),
                        meta.uncompressedBytes, Math.floor(fraction * count), count);
                    done = true;
                } catch (error) {
                    if (retry === 1 || cancel.signal.aborted) throw error;
                }
            }
        }
    };

    try {
        await Promise.all(Array.from({ length: Math.min(4, segments.length) }, () => worker()));
    } catch (error) {
        cancel.abort();
        throw error;
    }
    if (complete !== size) throw new Error('Parallel bundle is incomplete');
    console.info('[Web] Startup pack fetched in ' + segments.length + ' parallel ranges');
    return packed;
}

async function downloadSinglePack(meta, onProgress, count) {
    const response = await fetch(meta.url);
    if (!response.ok || !response.body) throw new Error('Startup pack unavailable');
    const reader = response.body.getReader();
    const chunks = [];
    let received = 0;
    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
        received += value.byteLength;
        if (received > meta.compressedBytes) throw new Error('Oversized startup pack');
        const fraction = received / meta.compressedBytes;
        onProgress(Math.floor(fraction * meta.uncompressedBytes),
            meta.uncompressedBytes, Math.floor(fraction * count), count);
    }
    if (received !== meta.compressedBytes) throw new Error('Truncated startup pack');
    const packed = new Uint8Array(received);
    let offset = 0;
    for (const chunk of chunks) {
        packed.set(chunk, offset);
        offset += chunk.byteLength;
    }
    return packed;
}


// IndexedDB retains the public, immutable startup archive across browser
// visits. HTTP 206 byte-range responses are not reliably cached by browsers;
// storing the verified archive is what makes a second login fast. Never store
// account credentials or game saves here; this contains public game assets.
const StartupCacheDatabase = 'everpt-public-assets';
const StartupCacheStore = 'startup-packs';
const StartupCacheKey = 'latest';

function openStartupDatabase() {
    return new Promise(resolve => {
        if (!('indexedDB' in globalThis)) return resolve(null);
        let request;
        try { request = indexedDB.open(StartupCacheDatabase, 1); }
        catch { return resolve(null); }
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(StartupCacheStore))
                request.result.createObjectStore(StartupCacheStore);
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => resolve(null);
        request.onblocked = () => resolve(null);
    });
}

function sha256(data) {
    return crypto.subtle.digest('SHA-256', data).then(bytes =>
        Array.from(new Uint8Array(bytes)).map(b => b.toString(16).padStart(2,'0')).join(''));
}

async function readStartupFromDisk(meta) {
    const db = await openStartupDatabase();
    if (!db) return null;
    try {
        const entry = await new Promise((resolve,reject) => {
            const request = db.transaction(StartupCacheStore,'readonly')
                .objectStore(StartupCacheStore).get(StartupCacheKey);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        if (!entry || entry.sha256 !== meta.sha256 ||
            entry.data?.size !== meta.compressedBytes) return null;
        const bytes = new Uint8Array(await entry.data.arrayBuffer());
        if (await sha256(bytes) !== meta.sha256) return null;
        return bytes;
    } catch {
        return null;
    } finally {
        db.close();
    }
}

function saveStartupToDisk(meta, compressed) {
    // Defer disk writes until the game has had a chance to render its first
    // frame. Quota and private browsing restrictions are optional fallbacks.
    setTimeout(async () => {
        try {
            if (navigator.storage?.estimate) {
                const {usage=0,quota=0}=await navigator.storage.estimate();
                if (quota && quota - usage < meta.compressedBytes * 1.2) return;
            }
            const db=await openStartupDatabase();
            if (!db) return;
            try {
                await new Promise((resolve,reject) => {
                    const tx=db.transaction(StartupCacheStore,'readwrite');
                    tx.objectStore(StartupCacheStore).put({
                        sha256:meta.sha256,
                        data:new Blob([compressed],{type:'application/octet-stream'}),
                    },StartupCacheKey);
                    tx.oncomplete=resolve;
                    tx.onerror=()=>reject(tx.error);
                    tx.onabort=()=>reject(tx.error);
                });
                console.info('[Web] Startup pack saved for future visits');
            } finally {
                db.close();
            }
        } catch { /* IDB is optional: browser or quota may deny it. */ }
    }, 15000);
}

async function loadVerifiedStartupPack(meta,onProgress,count) {
    const cached=await readStartupFromDisk(meta);
    if (cached) {
        onProgress(meta.uncompressedBytes,meta.uncompressedBytes,count,count);
        console.info('[Web] Startup pack loaded from persistent browser cache');
        return cached;
    }
    const downloaded=await downloadCompressedPack(meta,onProgress,count);
    if (await sha256(downloaded)!==meta.sha256)
        throw new Error('Startup pack integrity check failed');
    saveStartupToDisk(meta,downloaded);
    return downloaded;
}
