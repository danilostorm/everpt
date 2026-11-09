// What the site's panels share: the tool buttons over the game, the dialog and the form controls.
import { setInputEnabled } from './mgweb.js';
import { t } from './i18n.js';

let dialog, boxTitle, boxBody, tools, current = null, currentTitle = '';

function ensureDialog() {
    if (dialog) {
        return;
    }
    tools = document.createElement('div');
    tools.id = 'tools';
    document.body.appendChild(tools);
    dialog = document.createElement('div');
    dialog.id = 'panel';
    dialog.hidden = true;
    dialog.setAttribute('role', 'dialog');
    dialog.setAttribute('aria-modal', 'true');
    dialog.innerHTML = '<div class="mg-box"><div class="mg-head"><h2></h2><button type="button" class="mg-button" id="panelClose">Close</button></div><div class="mg-body"></div></div>';
    document.body.appendChild(dialog);
    boxTitle = dialog.querySelector('h2');
    boxBody = dialog.querySelector('.mg-body');
    dialog.querySelector('#panelClose').textContent = t('Close');
    dialog.querySelector('#panelClose').addEventListener('click', closePanel);
    document.addEventListener('everpt-language-changed', () => {
        dialog.querySelector('#panelClose').textContent = t('Close');
        if (currentTitle) boxTitle.textContent = t(currentTitle);
        for (const button of tools.querySelectorAll('button[data-original-label]')) {
            button.textContent = t(button.dataset.originalLabel);
        }
    });
    dialog.addEventListener('mousedown', event => {
        if (event.target === dialog) {
            closePanel();
        }
    });
    window.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !dialog.hidden) {
            event.stopPropagation();
            closePanel();
        }
    }, true);
}

export function tool(label, run, hidden = false) {
    ensureDialog();
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.originalLabel = label;
    button.textContent = t(label);
    button.hidden = hidden;
    button.addEventListener('click', run);
    tools.appendChild(button);
    return button;
}

export function openPanel(title, body) {
    ensureDialog();
    if (current) {
        current.remove();
    }
    currentTitle = title;
    boxTitle.textContent = t(title);
    boxBody.appendChild(body);
    current = body;
    dialog.hidden = false;
    setInputEnabled(false);
    dialog.querySelector('#panelClose').focus();
}

export function closePanel() {
    if (!dialog || dialog.hidden) {
        return;
    }
    dialog.hidden = true;
    setInputEnabled(true);
    document.getElementById('game').focus();
}

export function panelIsOpen(body) {
    return dialog && !dialog.hidden && current === body;
}

// A labelled row: { label, help, type: toggle | select | range, options, min, max, step, format }. Returns its element and setValue.
export function control(row, onChange) {
    const element = document.createElement('div');
    element.className = 'mg-row';
    const label = document.createElement('label');
    label.textContent = row.label;
    const holder = document.createElement('div');
    holder.className = 'mg-control';
    let setValue;
    if (row.type === 'toggle') {
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.addEventListener('change', () => onChange(input.checked));
        holder.appendChild(input);
        setValue = value => {
            input.checked = !!value;
        };
        label.htmlFor = input.id = `mg-${row.key || row.site}`;
    } else if (row.type === 'select') {
        const input = document.createElement('select');
        for (const [value, text] of row.options) {
            const option = document.createElement('option');
            option.value = String(value);
            option.textContent = String(text);
            input.appendChild(option);
        }
        input.addEventListener('change', () => {
            const numeric = typeof row.options[0][0] === 'number';
            onChange(numeric ? +input.value : input.value);
        });
        holder.appendChild(input);
        setValue = value => {
            input.value = String(value);
        };
        label.htmlFor = input.id = `mg-${row.key || row.site}`;
    } else {
        const input = document.createElement('input');
        input.type = 'range';
        input.min = row.min;
        input.max = row.max;
        input.step = row.step;
        const shown = document.createElement('span');
        shown.className = 'mg-value';
        input.addEventListener('input', () => {
            shown.textContent = row.format(+input.value);
            onChange(+input.value);
        });
        holder.append(input, shown);
        setValue = value => {
            input.value = value;
            shown.textContent = row.format(+input.value);
        };
        label.htmlFor = input.id = `mg-${row.key || row.site}`;
    }
    element.append(label, holder);
    if (row.help) {
        const help = document.createElement('div');
        help.className = 'mg-help';
        help.textContent = row.help;
        element.appendChild(help);
    }
    return { element, setValue };
}
