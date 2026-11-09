import { dotnet } from './_framework/dotnet.js';
import { imports, prepareGpu, requestScreenshot, runtimeStats, setUncapped } from './mgweb.js';
import { audioStats } from './mgaudio.js';
import { restoreSettings, startSettings } from './settings.js';
import { currentLanguage, isPortuguese, t, useLanguage } from './i18n.js';
import { startAdmin } from './admin.js';
import { fileStats, loadLog, mountFiles, preloadInBackground, preloadStartup, readManifest } from './mgfiles.js';

useLanguage(currentLanguage());
const loadingEl = document.getElementById('loading');
const titleEl = document.getElementById('title');
const barEl = document.getElementById('bar');
const fillEl = document.getElementById('fill');
const detailEl = document.getElementById('detail');
const errorEl = document.getElementById('error');
const params = new URLSearchParams(location.search);
const logLines = [];
const errors = [];

// fraction null: an activity without a known length.
function showProgress(title, fraction, detail = '', small = false) {
    loadingEl.hidden = false;
    loadingEl.classList.toggle('small', small && errors.length === 0);
    titleEl.textContent = t(title);
    barEl.classList.toggle('busy', fraction === null);
    if (fraction !== null) {
        fillEl.style.width = `${Math.max(2, Math.min(100, fraction * 100))}%`;
    }
    detailEl.textContent = t(detail);
}

function hideProgress() {
    if (errors.length === 0) {
        loadingEl.hidden = true;
    }
}

function megabytes(bytes) {
    return bytes >= 1e7 ? `${Math.round(bytes / 1e6)} MB` : `${(bytes / 1e6).toFixed(1)} MB`;
}

function log(line) {
    logLines.push(line);
    console.log(line);
    if (logLines.length > 4000) {
        logLines.splice(0, 1000);
    }
}

// Each distinct error once; what only follows from an earlier error says nothing new.
const seenErrors = new Map();
function fail(message) {
    if (message.includes('due to a previous error')) {
        return;
    }
    const key = message.replace(/\d+/g, '#');
    seenErrors.set(key, (seenErrors.get(key) || 0) + 1);
    if (seenErrors.get(key) === 1) {
        errors.push(message);
        loadingEl.hidden = false;
        loadingEl.classList.remove('small');
        errorEl.textContent = errors.slice(0, 6).join('\n');
    }
}

// Automated runs report to the page's own host, or to the dev host named by ?report= when testing a deployed site.
const isLocalDevelopment = location.hostname === 'localhost' || location.hostname === '127.0.0.1';
const reportBase = isLocalDevelopment ? (params.get('report') || '') : '';
const report = name => fetch(`${reportBase}/report/${name}`, { method: 'POST', mode: reportBase ? 'no-cors' : 'cors', body: logLines.join('\n') });

window.addEventListener('error', event => fail(String(event.message)));
window.addEventListener('unhandledrejection', event => fail(String(event.reason && event.reason.stack || event.reason)));

const started = performance.now();
showProgress('Checking your graphics card', null);
const info = await prepareGpu(document.getElementById('game'), fail).catch(error => {
    titleEl.textContent = t('This browser cannot run Etervale');
    barEl.hidden = true;
    fail(`${error.message} ${t('Etervale needs WebGPU: try a current version of Chrome or Edge.')}`);
    throw error;
});
log(`[Web] GPU ${info.vendor} ${info.architecture}, fallback ${info.isFallbackAdapter}.`);

showProgress('Downloading the game', null);
const runtime = await dotnet
    
    .withModuleConfig({
        print: text => log(text),
        printErr: text => log('[stderr] ' + text),
        onDownloadResourceProgress: (loaded, total) => showProgress('Downloading the game', total ? loaded / total : null, `${t('Program files')}: ${loaded} ${t('of')} ${total}`),
    })
    .withApplicationArguments(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws.ashx`)
    .create();
runtime.setModuleImports('mgweb', imports(runtime.Module));
showProgress('Downloading game data', null);
const preload = await readManifest();
const preloadStarted = performance.now();
await preloadStartup(preload.startup, (bytes, total, done, count) => {
    const seconds = (performance.now() - preloadStarted) / 1000;
    const speed = seconds > 0.5 ? `, ${(bytes / 1e6 / seconds).toFixed(1)} MB/s` : '';
    showProgress('Downloading game data', total ? bytes / total : null, `${megabytes(bytes)} ${t('of')} ${megabytes(total)}${speed} (${done} ${t('of')} ${count} ${t('files')})`);
});
log(`[Web] Start-up data in ${((performance.now() - preloadStarted) / 1000).toFixed(1)} s.`);
showProgress('Starting the game', null, 'Preparing the world...');
mountFiles(runtime.Module.FS, log);
restoreSettings(runtime.Module.FS);
log(`[Web] Ready in ${(performance.now() - started).toFixed(0)} ms.`);

// Once the game draws, the panel goes away and the first world's data follows in the background; a small bar shows while
// the game itself waits for a file.
let lastOnDemand = 0;
let lastOnDemandTime = 0;
let background = null;
const progressTimer = setInterval(() => {
    const now = performance.now();
    const onDemand = fileStats().onDemand;
    if (onDemand !== lastOnDemand) {
        lastOnDemand = onDemand;
        lastOnDemandTime = now;
    }
    if (runtimeStats().draws === 0) {
        return;
    }
    if (background === null) {
        background = { fraction: 0, detail: '', done: false };
        preloadInBackground(preload.world, (bytes, total) => {
            background.fraction = total ? bytes / total : 1;
            background.detail = `${megabytes(bytes)} ${t('of')} ${megabytes(total)}`;
        }).catch(error => log('[Web] background download: ' + error.message)).finally(() => {
            background.done = true;
            log(`[Web] World data cached at ${(performance.now() / 1000).toFixed(0)} s.`);
        });
    }
    if (now - lastOnDemandTime < 1500) {
        showProgress('Loading game files', null, '', true);
    } else if (!background.done) {
        showProgress('Preparing the world', background.fraction, background.detail, true);
    } else {
        hideProgress();
    }
}, 250);

// Automated runs (?auto=name&seconds=30): screenshots and the log go to the dev host.
if (isLocalDevelopment && params.has('auto')) {
    const beats = [];
    setInterval(() => {
        beats.push(`t=${(performance.now() / 1000).toFixed(0)} ${JSON.stringify(runtimeStats())} ${JSON.stringify(fileStats())} errors=${errors.length} ${errors.slice(0, 3).join(' / ')}`);
        navigator.sendBeacon(`${reportBase}/report/heartbeat`, beats.slice(-40).join('\n') + '\n--- log tail ---\n' + logLines.filter(l => !l.startsWith('[Probe]') && !l.startsWith('[Trace]')).slice(-40).join('\n'));
    }, 5000);
    const name = params.get('auto');
    const seconds = +(params.get('seconds') || 30);
    let shots = 0;
    const timer = setInterval(() => {
        requestScreenshot(`${name}-${++shots}`, log, reportBase);
        report(name + '-partial');
    }, seconds * 1000 / 3);
    setTimeout(async () => {
        clearInterval(timer);
        requestScreenshot(`${name}-final`, log, reportBase);
        await new Promise(resolve => setTimeout(resolve, 1500));
        log('[Web] runtime ' + JSON.stringify(runtimeStats()));
        log('[Web] files ' + JSON.stringify(fileStats()));
        log(`[Web] ${errors.length} distinct errors`);
        for (const message of errors) {
            log('ERROR ' + message);
        }
        await report(name);
    }, seconds * 1000);
}

// Automated runs can drive the game through its real input path: ?login=account:password.
const pressKey = async (code, key) => {
    window.dispatchEvent(new KeyboardEvent('keydown', { code, key }));
    await new Promise(resolve => setTimeout(resolve, 120));
    window.dispatchEvent(new KeyboardEvent('keyup', { code, key }));
    await new Promise(resolve => setTimeout(resolve, 120));
};
const typeText = async text => {
    for (const character of text) {
        const code = /[a-z]/i.test(character) ? 'Key' + character.toUpperCase() : /\d/.test(character) ? 'Digit' + character : character === '-' ? 'Minus' : 'Space';
        await pressKey(code, character);
    }
};
const clickAt = async (x, y) => {
    const canvas = document.getElementById('game');
    const rect = canvas.getBoundingClientRect();
    const init = { clientX: rect.left + x * rect.width / canvas.width, clientY: rect.top + y * rect.height / canvas.height, button: 0, bubbles: true };
    canvas.dispatchEvent(new MouseEvent('mousemove', init));
    await new Promise(resolve => setTimeout(resolve, 200));
    canvas.dispatchEvent(new MouseEvent('mousedown', init));
    await new Promise(resolve => setTimeout(resolve, 150));
    window.dispatchEvent(new MouseEvent('mouseup', init));
    await new Promise(resolve => setTimeout(resolve, 150));
};
// Never accept account passwords from URL parameters. Local automation uses
// window.etervale.pressKey/typeText directly, without leaking credentials into
// browser history, HTTP access logs, or referrer headers.

window.etervale = { audioStats, requestScreenshot, runtimeStats, fileStats, logLines, pressKey, typeText, loadLog, loginMark: 0 };
const programExports = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Program;
window.etervale.exports = programExports;
startSettings(programExports);
startAdmin(programExports);
await runtime.runMain();
