// The Settings panel: the game's own options (ClientSettings, applied live and saved in this browser) and the site's.
import { audioStats, setAudioLevels } from './mgaudio.js';
import { closePanel, control, openPanel, panelIsOpen, tool } from './panels.js';
import { browserLanguage, chooseLanguage, currentLanguage, resetLanguageChoice, startingLanguage, t, useLanguage } from './i18n.js';
import { runtimeStats, takeScreenshot } from './mgweb.js';

const SETTINGS_FILE = '/client-settings.json';
const STORE_GAME = 'etervale-client-settings';
const STORE_SITE = 'etervale-site-settings';
const SAVE_INTERVAL_MS = 2000;
const APPLY_DELAY_MS = 120;
const RESIZE_DELAY_MS = 400;
const MIN_WIDTH = 800, MIN_HEIGHT = 600;

const siteDefaults = {
    resolution: '1280x720', upscale: true, showFps: false, master: 80, music: 70, effects: 90, muted: false, muteHidden: false,
};

const onOff = { type: 'toggle' };
const size = values => values.map(value => [value, value]);

// key: the ClientSettings field. render: part of a graphics preset, so changing it makes the preset Custom.
const sections = [
    ['General', [
        { key: 'language', label: 'Language', type: 'select', options: [['pt-BR', 'Português (Brasil)'], ['en', 'English']] },
    ]],
    ['Display', [
        { site: 'resolution', label: 'Resolution', type: 'select', options: [['window', 'Fit window'], ['window75', 'Fit window, 75%'], ['window50', 'Fit window, 50%'], ['1280x720', '1280 x 720'], ['1600x900', '1600 x 900'], ['1920x1080', '1920 x 1080'], ['2560x1440', '2560 x 1440']], help: 'Fewer pixels run faster on weaker graphics cards.' },
        { site: 'upscale', label: 'Scale up to fit', ...onOff, help: 'Stretch the picture to fill the window.' },
        { key: 'maxFps', label: 'Frame limit', type: 'select', options: [[0, 'Display rate'], [30, '30 fps'], [60, '60 fps'], [120, '120 fps'], [144, '144 fps']] },
        { key: 'backgroundMaxFps', label: 'In a background tab', type: 'select', options: [[15, '15 fps'], [30, '30 fps'], [60, '60 fps'], [0, 'No limit']] },
        { key: 'uiScale', label: 'Interface size', type: 'range', min: 0.75, max: 1.5, step: 0.05, format: value => `${Math.round(value * 100)}%` },
        { site: 'showFps', label: 'Show frame rate', ...onOff },
    ]],
    ['Quality', [
        { key: 'antiAliasing', label: 'Anti-aliasing', type: 'select', render: true, options: [['Off', 'Off'], ['Fxaa', 'FXAA'], ['Smaa', 'SMAA'], ['Msaa', 'MSAA'], ['Taa', 'TAA']] },
        { key: 'ambientOcclusion', label: 'Ambient occlusion', ...onOff, render: true },
        { key: 'screenSpaceReflections', label: 'Reflections on water and ice', ...onOff, render: true },
        { key: 'imageBasedLighting', label: 'Sky lighting', ...onOff, render: true },
        { key: 'localLightLimit', label: 'Lamps and combat lights', type: 'select', render: true, options: size([8, 16, 32]) },
        { key: 'volumetricLight', label: 'Light shafts', ...onOff, render: true },
    ]],
    ['Shadows', [
        { key: 'shadowsEnabled', label: 'Shadows', ...onOff, render: true },
        { key: 'shadowMapSize', label: 'Detail', type: 'select', render: true, options: [[1024, 'Low (1024)'], [2048, 'Medium (2048)'], [4096, 'High (4096)'], [8192, 'Very high (8192)']] },
        { key: 'shadowCascades', label: 'Distance steps', type: 'select', render: true, options: size([1, 2, 3, 4]) },
        { key: 'shadowFilter', label: 'Edges', type: 'select', render: true, options: [['Pcf', 'Sharp'], ['Pcss', 'Soft']] },
        { key: 'shadowUpdateRate', label: 'Update rate', type: 'select', render: true, options: [[30, '30 Hz'], [60, '60 Hz'], [120, '120 Hz'], [0, 'Every frame']] },
        { key: 'contactShadows', label: 'Contact shadows', ...onOff, render: true },
        { key: 'lampShadowCount', label: 'Lamps that cast shadows', type: 'select', render: true, options: size([0, 2, 4]) },
        { key: 'lampShadowTileSize', label: 'Lamp shadow detail', type: 'select', render: true, options: size([256, 512, 1024]) },
    ]],
    ['Sky and weather', [
        { key: 'realisticSky', label: 'Realistic sky', ...onOff, render: true, help: 'Sun, moon, stars and clouds, instead of the original painted skies.' },
        { key: 'cloudQuality', label: 'Clouds', type: 'select', render: true, options: size(['Off', 'Low', 'Medium', 'High']) },
        { key: 'weatherEnabled', label: 'Weather', ...onOff, help: 'Rain, lightning and their sounds.' },
        { key: 'distanceFog', label: 'Distance haze', type: 'select', render: true, options: [['Off', 'Off'], ['Atmospheric', 'On']] },
    ]],
    ['Image', [
        { key: 'autoExposure', label: 'Automatic exposure', ...onOff, render: true },
        { key: 'exposureCompensation', label: 'Brightness', type: 'range', render: true, min: -3, max: 3, step: 0.25, format: value => `${value > 0 ? '+' : ''}${(+value).toFixed(2)} EV` },
        { key: 'tonemapper', label: 'Colour response', type: 'select', render: true, options: [['PbrNeutral', 'Neutral'], ['AgX', 'AgX (filmic)'], ['Aces', 'ACES (punchy)']] },
        { key: 'bloom', label: 'Bloom', type: 'select', render: true, options: size(['Off', 'Low', 'High']) },
        { key: 'colorGrading', label: 'Colour grade', ...onOff, render: true },
        { key: 'sharpening', label: 'Sharpening', type: 'range', render: true, min: 0, max: 1, step: 0.05, format: value => `${Math.round(value * 100)}%` },
    ]],
    ['Sound', [
        { site: 'muted', label: 'Mute everything', ...onOff },
        { site: 'master', label: 'Volume', type: 'range', min: 0, max: 100, step: 1, format: value => `${value}%` },
        { key: 'musicEnabled', label: 'Music', ...onOff },
        { site: 'music', label: 'Music and ambience volume', type: 'range', min: 0, max: 100, step: 1, format: value => `${value}%` },
        { key: 'soundEnabled', label: 'Sound effects', ...onOff },
        { site: 'effects', label: 'Effects volume', type: 'range', min: 0, max: 100, step: 1, format: value => `${value}%` },
        { site: 'muteHidden', label: 'Mute in a background tab', ...onOff },
    ]],
    ['Gameplay', [
        { key: 'classicTargetHealthBar', label: 'Classic target health bar', ...onOff, help: 'Shows only after you hit a monster at full health, without numbers.' },
        { key: 'classicNamePlates', label: 'Classic name plates', ...onOff },
        { key: 'levelGuide', label: 'Level guide notices', ...onOff },
    ]],
    ['Developer', [
        { key: 'calibrationSpheres', label: 'Calibration spheres', ...onOff, render: true, help: 'A grey and a chrome sphere beside the camera, for judging light.' },
    ]],
];

const presets = ['Low', 'Medium', 'High', 'Ultra'];

let FS, exports_;
let game = {};
let site = { ...siteDefaults };
let inputs = new Map();
let applyTimer = 0;
let presetButtons = [];
let fpsEl;

function readStored(key) {
    try {
        return JSON.parse(localStorage.getItem(key) || 'null');
    } catch {
        return null;
    }
}

function store(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch { /* blocked storage: the settings last for this visit */ }
}

function readGameFile() {
    try {
        return JSON.parse(FS.readFile(SETTINGS_FILE, { encoding: 'utf8' }));
    } catch {
        return null;
    }
}

// Before the game starts: what this browser saved.
export function restoreSettings(fileSystem) {
    FS = fileSystem;
    const saved = readStored(STORE_GAME);
    // Earlier builds wrote English as an implicit default even when the browser
    // preferred Portuguese. Prefer navigator.languages for those old defaults,
    // but preserve explicit language choices and existing pt-BR settings.
    const language = useLanguage(startingLanguage(saved));
    FS.writeFile(SETTINGS_FILE, JSON.stringify({ ...(saved || { version: 1 }), language, languageAutomatic: false }));
    site = { ...siteDefaults, ...readStored(STORE_SITE) };
    applySound();
}

function applySound() {
    setAudioLevels({ master: site.master / 100, music: site.music / 100, effects: site.effects / 100, muted: site.muted, muteHidden: site.muteHidden });
}

function saveSite() {
    store(STORE_SITE, JSON.stringify(site));
}

function sendGame() {
    clearTimeout(applyTimer);
    applyTimer = setTimeout(() => {
        exports_.ApplySettings(JSON.stringify(game));
        setTimeout(refreshFromGame, 600);
    }, APPLY_DELAY_MS);
}

function refreshFromGame() {
    const current = readGameFile();
    if (current) {
        game = current;
        store(STORE_GAME, JSON.stringify(current));
        refreshControls();
    }
}

function refreshControls() {
    for (const [row, input] of inputs) {
        const value = row.site ? site[row.site] : game[row.key];
        if (value !== undefined) {
            input.setValue(value);
        }
    }
    for (const button of presetButtons) {
        button.setAttribute('aria-pressed', String(game.quality === button.dataset.preset));
    }
}

function changed(row, value) {
    if (row.site) {
        site[row.site] = value;
        saveSite();
        applySound();
        fitCanvas();
        if (row.site === 'resolution') {
            applyResolution();
        }
        updateFpsVisibility();
        return;
    }
    game[row.key] = value;
    if (row.key === 'language') {
        game.language = chooseLanguage(value);
        game.languageAutomatic = false;
        sendGame();
        return;
    }
    if (row.render) {
        game.quality = 'Custom';
    }
    if (row.key === 'antiAliasing' && value !== 'Msaa') {
        game.multiSampleCount = 0;
    } else if (row.key === 'antiAliasing') {
        game.multiSampleCount = 4;
    }
    sendGame();
}

function targetSize() {
    const fit = { window: 1, window75: 0.75, window50: 0.5 }[site.resolution];
    if (fit) {
        const ratio = window.devicePixelRatio || 1;
        return [Math.max(MIN_WIDTH, Math.round(window.innerWidth * ratio * fit)), Math.max(MIN_HEIGHT, Math.round(window.innerHeight * ratio * fit))];
    }
    return site.resolution.split('x').map(Number);
}

function applyResolution() {
    const [width, height] = targetSize();
    if (game.windowWidth !== width || game.windowHeight !== height) {
        game.windowWidth = width;
        game.windowHeight = height;
        sendGame();
    }
}

function fitCanvas() {
    const canvas = document.getElementById('game');
    const scale = Math.min(window.innerWidth / canvas.width, window.innerHeight / canvas.height);
    const shown = site.upscale ? scale : Math.min(scale, 1);
    canvas.style.width = `${Math.floor(canvas.width * shown)}px`;
    canvas.style.height = `${Math.floor(canvas.height * shown)}px`;
}

function updateFpsVisibility() {
    fpsEl.hidden = !site.showFps;
}

function startFpsCounter() {
    fpsEl = document.createElement('div');
    fpsEl.id = 'fps';
    document.body.appendChild(fpsEl);
    updateFpsVisibility();
    let lastFrames = 0, lastTime = performance.now();
    setInterval(() => {
        const frames = runtimeStats().frames, now = performance.now();
        fpsEl.textContent = `${((frames - lastFrames) * 1000 / (now - lastTime)).toFixed(0)} fps`;
        lastFrames = frames;
        lastTime = now;
    }, 1000);
}

function download(name, blob) {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 10000);
}

function buildPanel() {
    const body = document.createElement('div');
    const presetRow = document.createElement('div');
    presetRow.className = 'mg-actions';
    presetRow.innerHTML = '<span class="mg-label"></span>';
    presetRow.querySelector('.mg-label').textContent = t('Graphics preset');
    const group = document.createElement('div');
    group.className = 'mg-seg';
    presetButtons = [...presets, 'Custom'].map(name => {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = t(name);
        button.dataset.preset = name;
        if (name === 'Custom') {
            button.disabled = true;
        }
        button.addEventListener('click', () => {
            game.quality = name;
            sendGame();
            for (const other of presetButtons) {
                other.setAttribute('aria-pressed', String(other === button));
            }
        });
        group.appendChild(button);
        return button;
    });
    presetRow.appendChild(group);
    body.appendChild(presetRow);

    for (const [title, rows] of sections) {
        const heading = document.createElement('h3');
        heading.textContent = t(title);
        body.appendChild(heading);
        for (const row of rows) {
            const translated = {
                ...row,
                label: t(row.label),
                help: row.help ? t(row.help) : undefined,
                options: row.options?.map(([key, label]) => [key, t(label)]),
            };
            const built = control(translated, value => changed(row, value));
            inputs.set(row, built);
            body.appendChild(built.element);
        }
    }

    const heading = document.createElement('h3');
    heading.textContent = t('Tools');
    body.appendChild(heading);
    const actions = document.createElement('div');
    actions.className = 'mg-actions';
    const action = (label, run, title = '') => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'mg-button';
        button.textContent = t(label);
        button.title = title ? t(title) : '';
        button.addEventListener('click', run);
        actions.appendChild(button);
    };
    action('Full screen', () => document.documentElement.requestFullscreen?.());
    action('Screenshot', () => takeScreenshot(blob => download(`etervale-${Date.now()}.png`, blob)));
    action('Performance overlay', () => {
        closePanel();
        const press = type => window.dispatchEvent(new KeyboardEvent(type, { code: 'F9', key: 'F9' }));
        press('keydown');
        setTimeout(() => press('keyup'), 150);
    }, 'The game\'s own overlay. F9 also toggles it.');
    action('Export settings', () => download('etervale-settings.json', new Blob([JSON.stringify({ game, site }, null, 2)], { type: 'application/json' })));
    action('Import settings', () => {
        const picker = document.createElement('input');
        picker.type = 'file';
        picker.accept = '.json,application/json';
        picker.addEventListener('change', async () => {
            try {
                const imported = JSON.parse(await picker.files[0].text());
                site = { ...siteDefaults, ...imported.site };
                game = { ...game, ...imported.game, serverAddress: game.serverAddress };
                if (imported.game?.language) { game.language = chooseLanguage(imported.game.language); game.languageAutomatic = false; }
                saveSite();
                applySound();
                sendGame();
                refreshControls();
            } catch (error) {
                window.alert(`${t('That file could not be read:')} ${error.message}`);
            }
        });
        picker.click();
    });
    action('Reset to defaults', () => {
        if (window.confirm(t('Put every setting back to its default?'))) {
            site = { ...siteDefaults };
            const language = resetLanguageChoice();
            game = { version: game.version, serverAddress: game.serverAddress, language, languageAutomatic: false };
            saveSite();
            applySound();
            sendGame();
            fitCanvas();
            updateFpsVisibility();
            refreshControls();
        }
    });
    action('Sound check', () => {
        const stats = audioStats();
        window.alert(`${t('Sound is')} ${stats.state}. ${t('Sounds played')}: ${stats.played}. ${t('Decoded')}: ${stats.decoded}.`);
    });
    body.appendChild(actions);
    return body;
}

// Once the game runs: the panel, and the file the game saves kept in this browser.
export function startSettings(programExports) {
    exports_ = programExports;
    startFpsCounter();
    let body = buildPanel();
    document.addEventListener('everpt-language-changed', () => {
        const opened = panelIsOpen(body);
        inputs.clear();
        presetButtons = [];
        body = buildPanel();
        refreshControls();
        if (opened) openPanel('Settings', body);
    });
    tool('Settings', () => {
        const current = readGameFile();
        if (current) {
            game = current;
        }
        refreshControls();
        openPanel('Settings', body);
    });
    let saved = '';
    setInterval(() => {
        const current = readGameFile();
        if (current) {
            game = current;
            // Language can also be changed from the native in-game settings.
            // Persist an explicit selection so the browser locale will not
            // override the player's choice on the next visit.
            if (current.language && current.language !== currentLanguage()) {
                chooseLanguage(current.language);
            }
            const text = JSON.stringify(current);
            if (text !== saved) {
                saved = text;
                store(STORE_GAME, text);
            }
        }
    }, SAVE_INTERVAL_MS);
    let resizeTimer = 0;
    window.addEventListener('resize', () => {
        fitCanvas();
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (site.resolution.startsWith('window')) {
                applyResolution();
            }
        }, RESIZE_DELAY_MS);
    });
    // The canvas changes size with the game's display settings.
    let shownWidth = 0, shownHeight = 0;
    setInterval(() => {
        const canvas = document.getElementById('game');
        if (canvas.width !== shownWidth || canvas.height !== shownHeight) {
            shownWidth = canvas.width;
            shownHeight = canvas.height;
            fitCanvas();
        }
    }, 500);
    fitCanvas();
    setTimeout(() => {
        const current = readGameFile();
        if (current) {
            game = current;
            applyResolution();
        }
    }, 1000);
}
