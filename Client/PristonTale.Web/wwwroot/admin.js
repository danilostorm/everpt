// The Admin panel: the game's game master commands as forms, for accounts with a game master level. It sends the same chat
// lines a game master could type: the server checks the level of each and records it in its audit trail, so this panel gives
// nobody anything. It only appears for those who already have a level. The commands are those of docs/chat-and-gm.md.
import { openPanel, panelIsOpen, tool } from './panels.js';

const POLL_MS = 1000;
const MAX_LOG_LINES = 300;
const LEVELS = ['Player', 'Guide', 'Moderator', 'Operator', 'Administrator'];
const GUIDE = 1, MODERATOR = 2, OPERATOR = 3, ADMINISTRATOR = 4;
const KIND_CLASS = { 0: 'server', 1: 'whisper', 2: 'system', 3: 'notice' };

const text = (name, label, placeholder = '') => ({ name, label, placeholder });
const pick = (name, label, options) => ({ name, label, options });
const player = text('name', 'Player', 'character name');

// send: {name} fills a field; a field left empty, with the space before it, is dropped.
const groups = [
    ['Players', [
        { label: 'Look up', level: GUIDE, fields: [player], send: '/username {name}' },
        { label: 'Find', level: MODERATOR, fields: [player], send: '/where {name}' },
        { label: 'Go to', level: MODERATOR, fields: [player], send: '/near {name}' },
        { label: 'Bring here', level: MODERATOR, fields: [player], send: '/call {name}' },
        { label: 'Kick', level: MODERATOR, fields: [player], send: '/kick {name}', danger: 'Disconnect {name}?' },
        { label: 'Kill', level: MODERATOR, fields: [player], send: '/kill {name}', danger: 'Kill {name}\'s character?' },
        { label: 'Lend my level', level: GUIDE, fields: [player], send: '/grant {name}', danger: 'Give {name} your game master level until they leave the world?' },
        { label: 'Look up account', level: GUIDE, fields: [text('account', 'Account')], send: '/userid {account}' },
        { label: 'Move character to account', level: MODERATOR, fields: [text('account', 'Account'), text('character', 'Offline character')], send: '/insert {account} {character}' },
        { label: 'Open a stored warehouse', level: MODERATOR, fields: [text('account', 'Account')], send: '/open_warehouse {account}' },
        { label: 'Players online', level: GUIDE, send: '/user' },
        { label: 'Players in this area', level: GUIDE, send: '/area_users' },
        { label: 'Connections', level: GUIDE, send: '/sockets' },
    ]],
    ['Announcements', [
        { label: 'Notice to everyone', level: MODERATOR, fields: [text('message', 'Message')], send: '/>{message}' },
        { label: 'Notice with next heartbeat', level: MODERATOR, fields: [text('message', 'Message')], send: '/>>{message}' },
    ]],
    ['Server', [
        { label: 'Set player limit', level: MODERATOR, fields: [text('limit', 'Players', 'empty shows it')], send: '/user_limit {limit}' },
        { label: 'Shut down in 8 minutes', level: MODERATOR, send: '/shutdown', danger: 'Shut the server down for everyone? Players get notices for 8 minutes, then the world stops.' },
        { label: 'Read a setting', level: ADMINISTRATOR, fields: [text('setting', 'Setting')], send: '/setting {setting}' },
        { label: 'Change a setting', level: ADMINISTRATOR, fields: [text('setting', 'Setting'), text('value', 'Value')], send: '/setting {setting} {value}', danger: 'Change {setting} to {value}?' },
        { label: 'Reload settings', level: ADMINISTRATOR, send: '/reload_settings' },
        { label: 'Blocked logins', level: GUIDE, send: '/show_blackuser' },
        { label: 'Block reset', level: GUIDE, fields: [text('name', 'Account')], send: '/reset_blackuser {name}' },
        { label: 'Unblock', level: GUIDE, fields: [text('name', 'Account')], send: '/free_blackuser {name}' },
        { label: 'Server time', level: GUIDE, send: '/localtime' },
        { label: 'Boss hour', level: GUIDE, send: '/get_bosstime' },
        { label: 'Gold spent per sink', level: GUIDE, send: '/get_currency' },
    ]],
    ['Weather', [
        { label: 'Set weather', level: MODERATOR, fields: [pick('weather', 'Weather', ['auto', 'clear', 'overcast', 'rain', 'storm']), text('density', 'Density', '1-255, empty is heavy')], send: '/weather {weather} {density}' },
        { label: 'Weather now', level: MODERATOR, send: '/weather' },
    ]],
    ['My character', [
        { label: 'Become invisible', level: GUIDE, send: '/hide' },
        { label: 'Become visible', level: GUIDE, send: '/show' },
        { label: 'Go to field', level: MODERATOR, fields: [text('field', 'Field number')], send: '/field {field}' },
        { label: 'Move to position', level: MODERATOR, fields: [text('x', 'X'), text('y', 'Y'), text('z', 'Z')], send: '/player_location {x} {y} {z}' },
        { label: 'Set level', level: ADMINISTRATOR, fields: [text('level', 'Level')], send: '/GiveMeExp! {level}' },
        { label: 'Add experience', level: ADMINISTRATOR, fields: [text('points', 'Points')], send: '/expup {points}' },
        { label: 'Add gold', level: ADMINISTRATOR, fields: [text('amount', 'Amount')], send: '/GiveMeMoney! {amount}' },
        { label: 'Open my warehouse', level: MODERATOR, send: '/open_warehouse' },
    ]],
    ['Items', [
        { label: 'Drop item', level: ADMINISTRATOR, fields: [text('item', 'Code or "name"')], send: '/@get {item}' },
        { label: 'Drop best copy', level: ADMINISTRATOR, fields: [text('item', 'Code'), text('job', 'Class 1-8', 'optional')], send: '/@get_perf {item} {job}' },
        { label: 'Give item to player', level: ADMINISTRATOR, fields: [player, text('item', 'Code')], send: '/@put {name} {item}', danger: 'Put {item} in {name}\'s bag?' },
        { label: 'Drop aged gear', level: OPERATOR, fields: [text('item', 'Code'), text('age', 'Aging level'), text('job', 'Class 1-8', 'optional')], send: '/AgingSetting {item} {age} {job}' },
        { label: 'Force item class', level: GUIDE, fields: [text('job', 'Class')], send: '/specific {job}' },
        { label: 'Force middle roll', level: GUIDE, fields: [text('value', 'Value')], send: '/Performance {value}' },
    ]],
    ['Monsters', [
        { label: 'Spawn', level: GUIDE, fields: [text('monster', 'Monster'), text('count', 'How many', '1-21')], send: '/monster {monster} {count}' },
        { label: 'Spawn as my pets', level: GUIDE, fields: [text('monster', 'Monster'), text('count', 'How many')], send: '/mymonster {monster} {count}' },
        { label: 'Event: replace spawns', level: GUIDE, fields: [text('monster', 'Monster'), text('minutes', 'Minutes'), text('chance', 'Chance per 1000')], send: '/event_monster {monster} {minutes} {chance}' },
        { label: 'Monsters in this area', level: GUIDE, send: '/area_monster' },
    ]],
];

let exports_, button, statusEl, logEl, inputEl, body;
const forms = [];
let level = 0, inWorld = false;

function compose(template, values) {
    return template.replace(/ ?\{(\w+)\}/g, (match, name) => {
        const value = (values[name] || '').trim();
        return value ? match.replace(`{${name}}`, value) : '';
    });
}

function addLog(line, className = '') {
    const row = document.createElement('div');
    row.textContent = line;
    row.className = className;
    logEl.appendChild(row);
    while (logEl.childElementCount > MAX_LOG_LINES) {
        logEl.firstElementChild.remove();
    }
    logEl.scrollTop = logEl.scrollHeight;
}

function send(line) {
    addLog(`> ${line}`, 'mine');
    exports_.SendCommand(line);
}

function buildAction(action) {
    const form = document.createElement('form');
    form.className = 'mg-command';
    const values = {};
    const controls = [];
    for (const field of action.fields || []) {
        const wrap = document.createElement('label');
        wrap.textContent = field.label;
        const input = field.options ? document.createElement('select') : document.createElement('input');
        if (field.options) {
            for (const option of field.options) {
                input.add(new Option(option, option));
            }
        } else {
            input.placeholder = field.placeholder;
            input.autocomplete = 'off';
            input.spellcheck = false;
        }
        input.dataset.name = field.name;
        wrap.appendChild(input);
        form.appendChild(wrap);
        controls.push(input);
    }
    const submit = document.createElement('button');
    submit.type = 'submit';
    submit.className = action.danger ? 'mg-button danger' : 'mg-button';
    submit.textContent = action.label;
    form.appendChild(submit);
    form.addEventListener('submit', event => {
        event.preventDefault();
        for (const input of controls) {
            values[input.dataset.name] = input.value;
        }
        if (action.danger && !window.confirm(compose(action.danger, values))) {
            return;
        }
        send(compose(action.send, values));
    });
    forms.push({ form, submit, required: action.level, controls });
    return form;
}

function buildPanel() {
    body = document.createElement('div');
    statusEl = document.createElement('p');
    statusEl.className = 'mg-note';
    body.appendChild(statusEl);

    logEl = document.createElement('div');
    logEl.className = 'mg-log';
    logEl.setAttribute('role', 'log');
    body.appendChild(logEl);
    const line = document.createElement('form');
    line.className = 'mg-command line';
    inputEl = document.createElement('input');
    inputEl.placeholder = 'Type any command, such as /localtime';
    inputEl.setAttribute('aria-label', 'Command');
    inputEl.autocomplete = 'off';
    inputEl.spellcheck = false;
    const run = document.createElement('button');
    run.type = 'submit';
    run.className = 'mg-button';
    run.textContent = 'Send';
    line.append(inputEl, run);
    line.addEventListener('submit', event => {
        event.preventDefault();
        if (inputEl.value.trim()) {
            send(inputEl.value.trim());
            inputEl.value = '';
        }
    });
    body.appendChild(line);

    for (const [title, actions] of groups) {
        const details = document.createElement('details');
        const summary = document.createElement('summary');
        summary.textContent = title;
        details.appendChild(summary);
        for (const action of actions) {
            details.appendChild(buildAction(action));
        }
        body.appendChild(details);
    }
}

function refreshAvailability() {
    statusEl.textContent = inWorld
        ? `Signed in as ${LEVELS[level] || level}. Every command is checked by the server and recorded in its audit trail.`
        : 'Enter the world to use these commands.';
    for (const { submit, required, controls } of forms) {
        const allowed = inWorld && level >= required;
        submit.disabled = !allowed;
        submit.title = level < required ? `Needs ${LEVELS[required]}` : '';
        for (const input of controls) {
            input.disabled = !allowed;
        }
    }
}

export function startAdmin(programExports) {
    exports_ = programExports;
    buildPanel();
    button = tool('Admin', () => {
        refreshAvailability();
        openPanel('Admin', body);
        logEl.scrollTop = logEl.scrollHeight;
    }, true);
    setInterval(async () => {
        const [head, ...lines] = (await exports_.ReadConsole()).split('\n');
        const [newLevel, world] = head.split('\t').map(Number);
        const changed = newLevel !== level || (world === 1) !== inWorld;
        level = newLevel;
        inWorld = world === 1;
        button.hidden = level === 0;
        for (const entry of lines) {
            const [kind, ...words] = entry.split('\t');
            addLog(words.join('\t'), KIND_CLASS[kind] || '');
        }
        if (changed || panelIsOpen(body)) {
            refreshAvailability();
        }
    }, POLL_MS);
}
