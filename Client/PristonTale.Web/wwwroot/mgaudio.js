// The browser's audio runtime: plays the queue of monogame/web/Audio.Web.cs through Web Audio. The numbers mirror MGA.Op.

const Op = {
    BufferPcm: 1, BufferDestroy: 2, VoiceCreate: 3, VoiceDestroy: 4, VoiceSetBuffer: 5, VoiceAppend: 6, VoicePlay: 7, VoicePause: 8, VoiceResume: 9, VoiceStop: 10,
    VoicePan: 11, VoicePitch: 12, VoiceVolume: 13, VoiceFilter: 14, VoiceClearFilter: 15, VoicePosition: 16,
};
const ControlWords = 1 << 18, HeadWord = 0, TailWord = 1, RingBase = 16, RingWords = ControlWords - RingBase;
// The game has no sound categories of its own: a long loop is music or an ambience, the rest are effects.
const MusicSeconds = 20;
const FilterTypes = ['lowpass', 'bandpass', 'highpass'];

let module_, control, context, master, musicBus, effectsBus, analyser;
const buffers = new Map(), voices = new Map();
const stats = { played: 0, decoded: 0, skipped: 0 };
const levels = { master: 1, music: 1, effects: 1, muted: false, muteHidden: false };

function applyLevels() {
    if (!context) {
        return;
    }
    const silent = levels.muted || (levels.muteHidden && document.hidden);
    master.gain.value = silent ? 0 : levels.master;
    musicBus.gain.value = levels.music;
    effectsBus.gain.value = levels.effects;
}

// Slider positions, felt as linear: 0 to 1.
export function setAudioLevels(next) {
    Object.assign(levels, next);
    applyLevels();
}

export function audioStats() {
    let peak = 0;
    if (analyser) {
        const samples = new Float32Array(analyser.fftSize);
        analyser.getFloatTimeDomainData(samples);
        peak = samples.reduce((largest, sample) => Math.max(largest, Math.abs(sample)), 0);
    }
    return { ...stats, state: context ? context.state : 'none', buffers: buffers.size, voices: voices.size, peak };
}

function ensureContext() {
    if (context) {
        return;
    }
    context = new AudioContext();
    master = context.createGain();
    musicBus = context.createGain();
    effectsBus = context.createGain();
    musicBus.connect(master);
    effectsBus.connect(master);
    master.connect(context.destination);
    analyser = context.createAnalyser();
    master.connect(analyser);
    applyLevels();
}

// Browsers keep a page silent until the player touches it.
function installResume() {
    const resume = () => {
        if (context && context.state === 'suspended') {
            context.resume();
        }
    };
    for (const type of ['pointerdown', 'keydown']) {
        window.addEventListener(type, resume, { capture: true });
    }
    document.addEventListener('visibilitychange', applyLevels);
}

function decode(buffer) {
    if (buffer.audio) {
        return buffer.audio;
    }
    const { pointer, bytes, bits, rate, channels } = buffer;
    const frames = Math.floor(bytes / (bits / 8 * channels));
    if (frames <= 0 || rate < 3000) {
        return null;
    }
    const audio = context.createBuffer(channels, frames, rate);
    const heap = module_.HEAPU8.buffer;
    const outputs = [];
    for (let c = 0; c < channels; c++) {
        outputs.push(audio.getChannelData(c));
    }
    if (bits === 16) {
        const samples = new Int16Array(heap, pointer, frames * channels);
        for (let f = 0, s = 0; f < frames; f++) {
            for (let c = 0; c < channels; c++) {
                outputs[c][f] = samples[s++] / 32768;
            }
        }
    } else if (bits === 8) {
        const samples = new Uint8Array(heap, pointer, frames * channels);
        for (let f = 0, s = 0; f < frames; f++) {
            for (let c = 0; c < channels; c++) {
                outputs[c][f] = (samples[s++] - 128) / 128;
            }
        }
    } else {
        const samples = new Float32Array(heap, pointer, frames * channels);
        for (let f = 0, s = 0; f < frames; f++) {
            for (let c = 0; c < channels; c++) {
                outputs[c][f] = samples[s++];
            }
        }
    }
    buffer.audio = audio;
    stats.decoded++;
    return audio;
}

function newVoice(rate, channels) {
    ensureContext();
    const voice = {
        buffer: null, source: null, looped: false, paused: false, offset: 0, startedAt: 0, pitch: 0, volume: 1, gain3d: 1, pan: 0, pan3d: 0,
        rate, channels, queueEnd: 0, queued: [],
        filter: context.createBiquadFilter(), gain: context.createGain(), panner: context.createStereoPanner(),
    };
    voice.filter.type = 'allpass';
    voice.filter.connect(voice.gain);
    voice.gain.connect(voice.panner);
    return voice;
}

function applyVoice(voice) {
    voice.gain.gain.value = Math.max(0, voice.volume * voice.gain3d);
    voice.panner.pan.value = Math.max(-1, Math.min(1, voice.pan + voice.pan3d));
}

function stopSource(voice) {
    if (voice.source) {
        voice.source.onended = null;
        try {
            voice.source.stop();
        } catch { /* it had ended */ }
        voice.source.disconnect();
        voice.source = null;
    }
}

function startSource(voice, offset) {
    stopSource(voice);
    voice.panner.disconnect();
    const audio = voice.buffer && decode(voice.buffer);
    if (!audio) {
        return;
    }
    voice.panner.connect(voice.looped && audio.duration >= MusicSeconds ? musicBus : effectsBus);
    const source = context.createBufferSource();
    source.buffer = audio;
    source.loop = voice.looped;
    if (voice.looped && voice.buffer.loopLength > 0) {
        source.loopStart = voice.buffer.loopStart / voice.buffer.rate;
        source.loopEnd = (voice.buffer.loopStart + voice.buffer.loopLength) / voice.buffer.rate;
    }
    source.playbackRate.value = Math.pow(2, voice.pitch);
    source.connect(voice.filter);
    source.onended = () => {
        if (voice.source === source) {
            voice.source = null;
        }
    };
    source.start(0, offset % audio.duration);
    voice.source = source;
    voice.offset = offset;
    voice.startedAt = context.currentTime;
    stats.played++;
}

function position(voice) {
    return voice.offset + (context.currentTime - voice.startedAt) * Math.pow(2, voice.pitch);
}

function appendPcm(voice, pointer, bytes) {
    if (!voice.rate || !voice.channels) {
        return;
    }
    const heap = module_.HEAPU8.buffer;
    const frames = Math.floor(bytes / (2 * voice.channels));
    if (frames <= 0) {
        return;
    }
    const audio = context.createBuffer(voice.channels, frames, voice.rate);
    const samples = new Int16Array(heap, pointer, frames * voice.channels);
    for (let c = 0; c < voice.channels; c++) {
        const output = audio.getChannelData(c);
        for (let f = 0; f < frames; f++) {
            output[f] = samples[f * voice.channels + c] / 32768;
        }
    }
    voice.panner.disconnect();
    voice.panner.connect(effectsBus);
    const source = context.createBufferSource();
    source.buffer = audio;
    source.connect(voice.filter);
    voice.queueEnd = Math.max(voice.queueEnd, context.currentTime);
    source.start(voice.queueEnd);
    voice.queueEnd += audio.duration;
    voice.queued.push(source);
    source.onended = () => voice.queued.splice(voice.queued.indexOf(source), 1);
}

function run(op, a) {
    switch (op) {
        case Op.BufferPcm:
            buffers.set(a[0], { pointer: a[1], bytes: a[2], bits: a[3], rate: a[4], channels: a[5], loopStart: a[6], loopLength: a[7], audio: null });
            break;
        case Op.BufferDestroy:
            buffers.delete(a[0]);
            break;
        case Op.VoiceCreate:
            voices.set(a[0], newVoice(a[1], a[2]));
            break;
        case Op.VoiceDestroy: {
            const voice = voices.get(a[0]);
            if (voice) {
                stopSource(voice);
                voice.panner.disconnect();
                voices.delete(a[0]);
            }
            break;
        }
        case Op.VoiceSetBuffer: {
            const voice = voices.get(a[0]);
            if (voice) {
                stopSource(voice);
                voice.buffer = buffers.get(a[1]) || null;
                voice.paused = false;
                voice.pan3d = 0;
                voice.gain3d = 1;
                applyVoice(voice);
            }
            break;
        }
        case Op.VoiceAppend: {
            const voice = voices.get(a[0]);
            if (voice) {
                appendPcm(voice, a[1], a[2]);
            }
            break;
        }
        case Op.VoicePlay: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.looped = a[1] !== 0;
                voice.paused = false;
                // A one-shot before the player has touched the page would all sound at once at the first click.
                if (context.state === 'running' || voice.looped) {
                    applyVoice(voice);
                    startSource(voice, 0);
                } else {
                    stats.skipped++;
                }
            }
            break;
        }
        case Op.VoicePause: {
            const voice = voices.get(a[0]);
            if (voice && voice.source) {
                voice.offset = position(voice);
                voice.paused = true;
                stopSource(voice);
            }
            break;
        }
        case Op.VoiceResume: {
            const voice = voices.get(a[0]);
            if (voice && voice.paused) {
                voice.paused = false;
                startSource(voice, voice.offset);
            }
            break;
        }
        case Op.VoiceStop: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.paused = false;
                stopSource(voice);
                for (const source of voice.queued.splice(0)) {
                    source.onended = null;
                    source.stop();
                }
                voice.queueEnd = 0;
            }
            break;
        }
        case Op.VoicePan: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.pan = float(a[1]);
                applyVoice(voice);
            }
            break;
        }
        case Op.VoicePitch: {
            const voice = voices.get(a[0]);
            if (voice) {
                if (voice.source) {
                    voice.offset = position(voice);
                    voice.startedAt = context.currentTime;
                }
                voice.pitch = float(a[1]);
                if (voice.source) {
                    voice.source.playbackRate.value = Math.pow(2, voice.pitch);
                }
            }
            break;
        }
        case Op.VoiceVolume: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.volume = float(a[1]);
                applyVoice(voice);
            }
            break;
        }
        case Op.VoiceFilter: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.filter.type = FilterTypes[a[1]] || 'allpass';
                voice.filter.Q.value = float(a[2]);
                voice.filter.frequency.value = Math.min(float(a[3]), context.sampleRate / 2);
            }
            break;
        }
        case Op.VoiceClearFilter: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.filter.type = 'allpass';
            }
            break;
        }
        case Op.VoicePosition: {
            const voice = voices.get(a[0]);
            if (voice) {
                voice.gain3d = float(a[1]);
                voice.pan3d = float(a[2]);
                applyVoice(voice);
            }
            break;
        }
    }
}

const floatView = new Float32Array(1), intView = new Int32Array(floatView.buffer);
function float(bits) {
    intView[0] = bits;
    return floatView[0];
}

const argument = new Array(16);
function pump() {
    const words = new Int32Array(module_.HEAPU8.buffer, control, ControlWords);
    let tail = Atomics.load(words, TailWord);
    const head = Atomics.load(words, HeadWord);
    try {
        while (tail !== head) {
            const count = words[RingBase + tail % RingWords];
            const op = words[RingBase + (tail + 1) % RingWords];
            for (let i = 2; i < count; i++) {
                argument[i - 2] = words[RingBase + (tail + i) % RingWords];
            }
            run(op, argument);
            tail += count;
            Atomics.store(words, TailWord, tail);
        }
    } catch (error) {
        console.error('[Audio]', error);
        Atomics.store(words, TailWord, head);
    }
    setTimeout(pump, 5);
}

export function startAudioRuntime(runtimeModule, pointer) {
    module_ = runtimeModule;
    control = pointer;
    ensureContext();
    installResume();
    pump();
}
