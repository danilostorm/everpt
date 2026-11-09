// The browser's graphics runtime: replays the command stream of monogame/web/WebStream.cs through WebGPU. The operation
// numbers mirror WebStream.Op and the enum values the fork's api_enums.h. See monogame/web/README.md.

const Op = {
    ResizeSwapchain: 1, BeginFrame: 2, Clear: 3, Present: 4, SetBlendState: 5, SetDepthStencilState: 6, SetRasterizerState: 7, SetViewport: 8, SetScissor: 9,
    SetRenderTargets: 10, SetConstantBuffer: 11, SetTexture: 12, SetSampler: 13, SetIndexBuffer: 14, SetVertexBuffer: 15, SetShader: 16, SetInputLayout: 17,
    Draw: 18, DrawIndexed: 19, DrawIndexedInstanced: 20, Resolve: 21, CreateBlendState: 22, CreateDepthStencilState: 23, CreateRasterizerState: 24, CreateSampler: 25,
    CreateBuffer: 26, DestroyBuffer: 27, BufferSetData: 28, CreateTexture: 29, DestroyTexture: 30, TextureSetData: 31, TextureGetData: 32, BufferGetData: 33,
    CreateInputLayout: 34, DestroyInputLayout: 35, CreateShader: 36, DestroyShader: 37, Dispatch: 38, DispatchIndirect: 39, SetShaderBuffer: 40, SetShaderImage: 41,
    DestroyState: 42, BackBufferGetData: 43,
};

const QueueHead = 0, QueueTail = 1, FramesAcknowledged = 3, EventHead = 6, EventTail = 7, QueueBase = 16, QueueLength = 256;
const EventBase = QueueBase + QueueLength * 2, EventWords = 6, ControlWords = 8192;
const EventCapacity = Math.floor((ControlWords - EventBase) / EventWords);
const StageVertex = 0, StagePixel = 1, StageCompute = 2;
const BindingTexture = 32, BindingSampler = 128, BindingWritable = 64;
const UniformAlign = 256, UniformArenaBytes = 64 * 1024 * 1024;
const BufferIndex = 0, BufferVertex = 1, BufferConstant = 2, BufferStorage = 3;
const WriteOverwrite = 0, WriteDiscard = 1, WriteNoOverwrite = 2;
const TextureFlagRenderTarget = 1, TextureFlagStorage = 2;
const ClearTarget = 1, ClearDepth = 2, ClearStencil = 4;

const blendFactors = ['one', 'zero', 'src', 'one-minus-src', 'src-alpha', 'one-minus-src-alpha', 'dst', 'one-minus-dst', 'dst-alpha', 'one-minus-dst-alpha', 'constant', 'one-minus-constant', 'src-alpha-saturated'];
const blendOperations = ['add', 'subtract', 'reverse-subtract', 'min', 'max'];
const compareFunctions = ['always', 'never', 'less', 'less-equal', 'equal', 'greater-equal', 'greater', 'not-equal'];
const stencilOperations = ['keep', 'zero', 'replace', 'increment-wrap', 'decrement-wrap', 'increment-clamp', 'decrement-clamp', 'invert'];
const addressModes = ['repeat', 'clamp-to-edge', 'mirror-repeat', 'clamp-to-edge'];
const topologies = ['triangle-list', 'triangle-strip', 'line-list', 'line-strip', 'point-list'];
const depthFormats = [null, 'depth16unorm', 'depth24plus', 'depth24plus-stencil8', 'depth32float'];
const vertexFormats = ['float32', 'float32x2', 'float32x3', 'float32x4', 'unorm8x4', 'uint8x4', 'sint16x2', 'sint16x4', 'snorm16x2', 'snorm16x4', 'float16x2', 'float16x4'];
const semanticUsages = { POSITION: 0, COLOR: 1, TEXCOORD: 2, NORMAL: 3, BINORMAL: 4, TANGENT: 5, BLENDINDICES: 6, BLENDWEIGHT: 7 };
// [WebGPU format, bytes per block, block size]
const surfaceFormats = {
    0: ['rgba8unorm', 4, 1], 1: ['rgba8unorm', 2, 1], 2: ['rgba8unorm', 2, 1], 3: ['rgba8unorm', 2, 1], 4: ['bc1-rgba-unorm', 8, 4], 5: ['bc2-rgba-unorm', 16, 4], 6: ['bc3-rgba-unorm', 16, 4],
    7: ['rg8snorm', 2, 1], 8: ['rgba8snorm', 4, 1], 9: ['rgb10a2unorm', 4, 1], 10: ['rg16unorm', 4, 1], 11: ['rgba16unorm', 8, 1], 12: ['r8unorm', 1, 1], 13: ['r32float', 4, 1],
    14: ['rg32float', 8, 1], 15: ['rgba32float', 16, 1], 16: ['r16float', 2, 1], 17: ['rg16float', 4, 1], 18: ['rgba16float', 8, 1], 19: ['rgba16float', 8, 1], 20: ['bgra8unorm', 4, 1],
    21: ['bgra8unorm', 4, 1], 30: ['rgba8unorm-srgb', 4, 1], 31: ['bgra8unorm-srgb', 4, 1], 32: ['bgra8unorm-srgb', 4, 1], 33: ['bc1-rgba-unorm-srgb', 8, 4], 34: ['bc2-rgba-unorm-srgb', 16, 4],
    35: ['bc3-rgba-unorm-srgb', 16, 4], 70: ['bc1-rgba-unorm', 8, 4], 120: ['depth32float', 4, 1],
};
const DepthSurface = 120;
const TextureCube = 2, Texture3D = 1;

import { startAudioRuntime } from './mgaudio.js';

let module_, device, context, canvas, control, adapterFeatures;
let heap8, heap32, heapF32, memoryBuffer;
let encoder = null, pass = null, encoderSerial = 1;
let onError = () => { };
const stats = { frames: 0, draws: 0, dispatches: 0, pipelines: 0, bindGroups: 0, replayMs: 0, gpuWaitMs: 0, chunks: 0, flushes: 0 };

const blendStates = new Map(), depthStates = new Map(), rasterStates = new Map(), samplers = new Map();
const buffers = new Map(), textures = new Map(), shaders = new Map(), inputLayouts = new Map();
const pipelineCache = new Map(), computePipelines = new Map(), bindGroupLayouts = new Map(), pipelineLayouts = new Map(), bindGroups = new Map(), nullTextures = new Map();
let nearestSampler, uniformArena, uniformCpu, uniformUsed = 0;
let backBuffer = null, backDepth = null, backDepthFormat = null, backWidth = 0, backHeight = 0;

const state = {
    blend: 0, blendConstant: [1, 1, 1, 1], depth: 0, raster: 0,
    viewport: [0, 0, 1, 1, 0, 1], scissor: [0, 0, 1, 1], targets: [], indexBuffer: 0, indexFormat: 'uint16', layout: 0,
    vertexBuffers: [], shader: [0, 0, 0],
    cbuffers: [[], [], []], textures: [[], [], []], samplers: [[], [], []], storage: [new Map(), new Map(), new Map()],
    pendingClear: null, pipeline: null, passDirty: true, groupsDirty: [true, true], dynamic: [[], []], groups: [null, null],
    vertexDirty: true, indexDirty: true, viewportDirty: true, scissorDirty: true,
};

function refreshHeap() {
    const buffer = module_.HEAPU8.buffer;
    if (buffer !== memoryBuffer) {
        memoryBuffer = buffer;
        heap8 = new Uint8Array(buffer);
        heap32 = new Int32Array(buffer);
        heapF32 = new Float32Array(buffer);
    }
}

function report(message) {
    console.error(message);
    onError(message);
}

// ---- Shaders ----------------------------------------------------------------------------------------------------

const bindingPattern = /@group\((\d+)\)\s*@binding\((\d+)\)\s*var(?:<([^>]+)>)?\s+(\w+)\s*:\s*([^;]+);/g;
const inputPattern = /@location\((\d+)\)\s*(\w+)\s*:\s*([^,)]+)/g;

function parseShader(code, stage) {
    const bindings = [];
    for (const m of code.matchAll(bindingPattern)) {
        bindings.push({ group: +m[1], binding: +m[2], space: m[3] || '', name: m[4], type: m[5].trim() });
    }
    const entry = code.match(/@(?:vertex|fragment|compute)(?:\s*@workgroup_size\([^)]*\))?\s*fn\s+(\w+)/);
    const info = { module: device.createShaderModule({ code }), stage, entry: entry ? entry[1] : 'main', bindings, inputs: [], outputs: 0 };
    if (stage === StageVertex) {
        const start = code.indexOf('fn ' + info.entry + '(');
        const header = code.slice(start, code.indexOf('{', start));
        let position = 0;
        for (const m of header.matchAll(inputPattern)) {
            const semantic = m[2].match(/var_u002e_([A-Z]+?)(\d+)_*\d*$/);
            info.inputs.push({ location: +m[1], usage: semantic ? semanticUsages[semantic[1]] : -1, index: semantic ? +semantic[2] : 0, position: position++, type: m[3].trim() });
        }
    } else if (stage === StagePixel) {
        const body = code.slice(code.search(/@fragment/));
        const returned = body.match(/->\s*(@location\((\d+)\)\s*)?([\w<>]+)/);
        if (returned) {
            if (returned[2] !== undefined) {
                info.outputs |= 1 << +returned[2];
            } else {
                const structure = code.match(new RegExp('struct\\s+' + returned[3] + '\\s*\\{([^}]*)\\}'));
                if (structure) {
                    for (const m of structure[1].matchAll(/@location\((\d+)\)/g)) {
                        info.outputs |= 1 << +m[1];
                    }
                }
            }
        }
    }
    return info;
}

function textureDescriptor(binding, texture) {
    const m = binding.type.match(/^texture_(depth_)?(multisampled_)?(2d_array|2d|3d|cube_array|cube)(?:<(\w+)>)?/);
    const dimension = m ? m[3].replace('_', '-') : '2d';
    let sampleType = 'float';
    if (m && m[1]) {
        sampleType = 'depth';
    } else if (m && m[4] === 'u32') {
        sampleType = 'uint';
    } else if (m && m[4] === 'i32') {
        sampleType = 'sint';
    } else if (texture && texture.unfilterable) {
        sampleType = 'unfilterable-float';
    }
    return { dimension, sampleType };
}

function nullTexture(dimension) {
    let view = nullTextures.get(dimension);
    if (!view) {
        const descriptor = { format: 'rgba8unorm', usage: GPUTextureUsage.TEXTURE_BINDING, size: [1, 1, 1] };
        if (dimension === '3d') {
            descriptor.dimension = '3d';
        } else if (dimension === 'cube' || dimension === 'cube-array') {
            descriptor.size = [1, 1, 6];
        }
        view = device.createTexture(descriptor).createView({ dimension });
        nullTextures.set(dimension, view);
    }
    return view;
}

function textureView(texture, dimension) {
    let view = texture.views.get(dimension);
    if (!view) {
        view = texture.resolve.createView({ dimension, aspect: texture.depthSurface ? 'depth-only' : 'all' });
        texture.views.set(dimension, view);
    }
    return view;
}

// The layout and bind group of one stage for what is bound now. A depth surface or an unfilterable format needs its own
// layout, so a layout belongs to the resources' kinds; both are cached by key.
function resolveGroup(stage, groupIndex, shader) {
    const entries = [];
    const layoutEntries = [];
    let layoutKey = `${stage}:`;
    let groupKey = '';
    const dynamic = [];
    const visibility = stage === StageVertex ? GPUShaderStage.VERTEX : stage === StagePixel ? GPUShaderStage.FRAGMENT : GPUShaderStage.COMPUTE;
    for (const binding of shader.bindings) {
        if (binding.group !== groupIndex) {
            continue;
        }
        const number = binding.binding;
        if (binding.space === 'uniform') {
            const buffer = buffers.get(state.cbuffers[stage][number]);
            if (!buffer) {
                throw new Error(`no constant buffer at slot ${number} for ${binding.name}`);
            }
            const offset = uploadConstants(buffer);
            layoutEntries.push({ binding: number, visibility, buffer: { type: 'uniform', hasDynamicOffset: true } });
            entries.push({ binding: number, resource: { buffer: uniformArena, offset: 0, size: buffer.size } });
            layoutKey += `u${number},`;
            groupKey += `u${number}:${buffer.size},`;
            dynamic.push(offset);
        } else if (binding.space.startsWith('storage')) {
            const slot = binding.space.includes('read_write') ? number - BindingWritable : number - BindingTexture;
            const buffer = buffers.get(state.storage[stage].get(number) ?? 0);
            if (!buffer) {
                throw new Error(`no storage buffer for ${binding.name} (binding ${number}, slot ${slot})`);
            }
            const type = binding.space.includes('read_write') ? 'storage' : 'read-only-storage';
            layoutEntries.push({ binding: number, visibility, buffer: { type } });
            entries.push({ binding: number, resource: { buffer: buffer.gpu } });
            markUsed(buffer);
            layoutKey += `s${number}${type[0]},`;
            groupKey += `s${number}:${buffer.id}.${buffer.generation},`;
        } else if (binding.type.startsWith('texture_storage')) {
            const bound = state.storage[stage].get(number);
            const image = bound && textures.get(bound.texture);
            if (!image) {
                throw new Error(`no storage image for ${binding.name}`);
            }
            const format = binding.type.match(/<(\w+)/)[1];
            layoutEntries.push({ binding: number, visibility, storageTexture: { access: 'write-only', format, viewDimension: '2d' } });
            let view = image.mipViews.get(bound.mip);
            if (!view) {
                view = image.resolve.createView({ baseMipLevel: bound.mip, mipLevelCount: 1 });
                image.mipViews.set(bound.mip, view);
            }
            entries.push({ binding: number, resource: view });
            layoutKey += `i${number}${format},`;
            groupKey += `i${number}:${image.id}.${bound.mip},`;
        } else if (binding.type.startsWith('sampler')) {
            const slot = number - BindingSampler;
            const comparison = binding.type === 'sampler_comparison';
            let sampler = samplers.get(state.samplers[stage][slot]);
            const paired = textures.get(state.textures[stage][slot]);
            let type;
            let gpu;
            if (comparison) {
                type = 'comparison';
                gpu = sampler ? sampler.comparison : nearestSampler;
            } else if (sampler && sampler.filtering && !(paired && paired.unfilterable)) {
                type = 'filtering';
                gpu = sampler.gpu;
            } else {
                type = 'non-filtering';
                gpu = sampler && !sampler.filtering ? sampler.gpu : nearestSampler;
            }
            layoutEntries.push({ binding: number, visibility, sampler: { type } });
            entries.push({ binding: number, resource: gpu });
            layoutKey += `p${number}${type[0]},`;
            groupKey += `p${number}:${sampler ? sampler.id : 0}${type[0]},`;
        } else {
            const slot = number - BindingTexture;
            const texture = textures.get(state.textures[stage][slot]);
            const { dimension, sampleType } = textureDescriptor(binding, texture);
            layoutEntries.push({ binding: number, visibility, texture: { sampleType, viewDimension: dimension } });
            if (texture) {
                markUsed(texture);
            }
            entries.push({ binding: number, resource: texture ? textureView(texture, dimension) : nullTexture(dimension) });
            layoutKey += `t${number}${dimension}${sampleType[0]}${sampleType[1]},`;
            groupKey += `t${number}:${texture ? texture.id : 0},`;
        }
    }
    let layout = bindGroupLayouts.get(layoutKey);
    if (!layout) {
        layout = device.createBindGroupLayout({ entries: layoutEntries });
        layout.key = bindGroupLayouts.size;
        bindGroupLayouts.set(layoutKey, layout);
    }
    const key = layoutKey + '|' + groupKey;
    let group = bindGroups.get(key);
    if (!group) {
        group = device.createBindGroup({ layout, entries });
        stats.bindGroups++;
        bindGroups.set(key, group);
    }
    return { layout, group, dynamic };
}

function uploadConstants(buffer) {
    if (buffer.arenaSerial === encoderSerial) {
        return buffer.arenaOffset;
    }
    const aligned = (uniformUsed + UniformAlign - 1) & ~(UniformAlign - 1);
    if (aligned + buffer.size > UniformArenaBytes) {
        throw new Error('uniform arena full');
    }
    uniformCpu.set(buffer.cpu, aligned);
    uniformUsed = aligned + buffer.size;
    buffer.arenaOffset = aligned;
    buffer.arenaSerial = encoderSerial;
    return aligned;
}

function markUsed(resource) {
    resource.usedSerial = encoderSerial;
}

// ---- Passes -----------------------------------------------------------------------------------------------------

function ensureEncoder() {
    if (!encoder) {
        encoder = device.createCommandEncoder();
    }
    return encoder;
}

function currentAttachments() {
    if (state.targets.length === 0) {
        return { colors: [{ format: 'rgba8unorm', view: backBuffer.createView(), sampleCount: 1 }], depth: backDepth, depthFormat: backDepthFormat, sampleCount: 1, width: backWidth, height: backHeight };
    }
    const colors = [];
    let depth = null;
    let depthFormat = null;
    const first = textures.get(state.targets[0].id);
    for (const target of state.targets) {
        const texture = textures.get(target.id);
        markUsed(texture);
        if (texture.depthSurface) {
            depth = texture.resolve;
            depthFormat = texture.format;
            continue;
        }
        const viewKey = target.slice;
        let view = texture.sliceViews.get(viewKey);
        if (!view) {
            const descriptor = { dimension: '2d', baseArrayLayer: texture.type === Texture3D ? 0 : target.slice, arrayLayerCount: 1, baseMipLevel: 0, mipLevelCount: 1 };
            view = { color: (texture.multisample || texture.resolve).createView(descriptor), resolve: texture.multisample ? texture.resolve.createView(descriptor) : null };
            texture.sliceViews.set(viewKey, view);
        }
        colors.push({ format: texture.format, view: view.color, resolveTarget: view.resolve, sampleCount: texture.sampleCount });
        if (texture.depthTexture) {
            depth = texture.depthTexture;
            depthFormat = texture.depthFormat;
        }
    }
    return { colors, depth, depthFormat, sampleCount: first.sampleCount, width: first.width, height: first.height };
}

function ensurePass() {
    if (pass) {
        return pass;
    }
    ensureEncoder();
    const attachments = currentAttachments();
    state.attachments = attachments;
    const clear = state.pendingClear;
    state.pendingClear = null;
    const descriptor = {
        colorAttachments: attachments.colors.map(color => ({
            view: color.view, resolveTarget: color.resolveTarget || undefined,
            loadOp: clear && (clear.options & ClearTarget) ? 'clear' : 'load', clearValue: clear ? clear.color : undefined, storeOp: 'store'
        }))
    };
    if (attachments.depth) {
        const hasStencil = attachments.depthFormat.includes('stencil');
        const attachment = {
            view: attachments.depth.createView(),
            depthLoadOp: clear && (clear.options & ClearDepth) ? 'clear' : 'load', depthClearValue: clear ? clear.depth : 1, depthStoreOp: 'store'
        };
        if (hasStencil) {
            attachment.stencilLoadOp = clear && (clear.options & ClearStencil) ? 'clear' : 'load';
            attachment.stencilClearValue = clear ? clear.stencil : 0;
            attachment.stencilStoreOp = 'store';
        }
        descriptor.depthStencilAttachment = attachment;
    }
    pass = encoder.beginRenderPass(descriptor);
    state.passDirty = true;
    state.pipeline = null;
    state.vertexDirty = state.indexDirty = state.viewportDirty = state.scissorDirty = true;
    state.groupsDirty[0] = state.groupsDirty[1] = true;
    return pass;
}

function endPass(flushClear = true) {
    if (!pass && flushClear && state.pendingClear) {
        ensurePass();
    }
    if (pass) {
        pass.end();
        pass = null;
    }
}

// Submits everything recorded. The constants recorded so far go first in the queue; what binds them afterwards uploads again.
function flushEncoder() {
    endPass();
    if (encoder) {
        if (uniformUsed > 0) {
            device.queue.writeBuffer(uniformArena, 0, uniformCpu, 0, (uniformUsed + 3) & ~3);
        }
        device.queue.submit([encoder.finish()]);
        encoder = null;
        stats.flushes++;
    }
    uniformUsed = 0;
    encoderSerial++;
    state.groupsDirty[0] = state.groupsDirty[1] = true;
}

// ---- Draws ------------------------------------------------------------------------------------------------------

function blendDescriptor(state_, index) {
    const b = state_.targets[index];
    const opaque = b.colorSource === 0 && b.colorDest === 1 && b.alphaSource === 0 && b.alphaDest === 1 && b.colorFunction === 0 && b.alphaFunction === 0;
    if (opaque) {
        return undefined;
    }
    const factor = (value, alpha) => {
        let name = blendFactors[value];
        if (alpha) {
            name = name.replace('-src-alpha', '§').replace('src', 'src-alpha').replace('§', '-src-alpha');
            name = name === 'src-alpha-alpha' ? 'src-alpha' : name;
        }
        return name;
    };
    return {
        color: { srcFactor: factor(b.colorSource), dstFactor: factor(b.colorDest), operation: blendOperations[b.colorFunction] },
        alpha: { srcFactor: alphaFactor(b.alphaSource), dstFactor: alphaFactor(b.alphaDest), operation: blendOperations[b.alphaFunction] },
    };
}

// In an alpha blend, a colour factor means its alpha counterpart.
function alphaFactor(value) {
    switch (value) {
        case 2: return 'src-alpha';
        case 3: return 'one-minus-src-alpha';
        case 6: return 'dst-alpha';
        case 7: return 'one-minus-dst-alpha';
        default: return blendFactors[value];
    }
}

function stencilFace(compare, fail, depthFail, passOp) {
    return { compare: compareFunctions[compare], failOp: stencilOperations[fail], depthFailOp: stencilOperations[depthFail], passOp: stencilOperations[passOp] };
}

function vertexBuffersDescriptor(vertexShader, layout) {
    const streams = layout.strides.map(stride => ({ arrayStride: stride, stepMode: 'vertex', attributes: [] }));
    for (const element of layout.elements) {
        const input = vertexShader.inputs.find(i => i.usage === element.usage && i.index === element.index);
        if (!input) {
            continue;
        }
        const isInteger = /[ui]32/.test(input.type);
        let format = vertexFormats[element.format];
        if (!isInteger && format === 'uint8x4') {
            format = 'unorm8x4';
        } else if (!isInteger && format.startsWith('sint16')) {
            format = format.replace('sint', 'snorm');
        }
        const stream = streams[element.slot];
        if (element.stepRate > 0) {
            stream.stepMode = 'instance';
        }
        stream.attributes.push({ shaderLocation: input.location, offset: element.offset, format });
    }
    return streams;
}

function applyGroups(renderPass) {
    const vertexShader = shaders.get(state.shader[StageVertex]);
    const pixelShader = shaders.get(state.shader[StagePixel]);
    let changedLayout = false;
    if (state.groupsDirty[0]) {
        const resolved = resolveGroup(StageVertex, 0, vertexShader);
        changedLayout = changedLayout || state.groups[0]?.layout !== resolved.layout;
        state.groups[0] = resolved;
    }
    if (state.groupsDirty[1]) {
        const resolved = resolveGroup(StagePixel, 1, pixelShader);
        changedLayout = changedLayout || state.groups[1]?.layout !== resolved.layout;
        state.groups[1] = resolved;
    }
    return changedLayout;
}

function pipelineFor(topologyIndex, attachments) {
    const vs = state.shader[StageVertex], ps = state.shader[StagePixel];
    const formats = attachments.colors.map(c => c.format).join();
    const key = `${vs}|${ps}|${state.layout}|${state.blend}|${state.depth}|${state.raster}|${topologyIndex}|${formats}|${attachments.depthFormat}|${attachments.sampleCount}|${state.groups[0].layout.key}|${state.groups[1].layout.key}|${state.indexFormat}`;
    let pipeline = pipelineCache.get(key);
    if (pipeline) {
        return pipeline;
    }
    const vertexShader = shaders.get(vs), pixelShader = shaders.get(ps), layout = inputLayouts.get(state.layout);
    const blend = blendStates.get(state.blend), depthState = depthStates.get(state.depth), raster = rasterStates.get(state.raster);
    const topology = topologies[topologyIndex];
    const layoutKey = `${state.groups[0].layout.key},${state.groups[1].layout.key}`;
    let pipelineLayout = pipelineLayouts.get(layoutKey);
    if (!pipelineLayout) {
        pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [state.groups[0].layout, state.groups[1].layout] });
        pipelineLayouts.set(layoutKey, pipelineLayout);
    }
    const descriptor = {
        layout: pipelineLayout,
        vertex: { module: vertexShader.module, entryPoint: vertexShader.entry, buffers: vertexBuffersDescriptor(vertexShader, layout) },
        primitive: {
            topology,
            stripIndexFormat: topology.endsWith('strip') ? state.indexFormat : undefined,
            // CullClockwiseFace (1) discards clockwise faces, CullCounterClockwiseFace (2) counter-clockwise ones.
            frontFace: 'ccw',
            cullMode: raster ? ['none', 'back', 'front'][raster.cull] : 'none',
            unclippedDepth: raster && !raster.depthClip && adapterFeatures.has('depth-clip-control') ? true : undefined,
        },
        multisample: { count: attachments.sampleCount },
    };
    const targets = attachments.colors.map((color, index) => {
        const target = { format: color.format };
        const written = pixelShader.outputs & (1 << index);
        if (blend && !/^(rgba32|r32|rg32)/.test(color.format)) {
            target.blend = blendDescriptor(blend, index);
        }
        target.writeMask = written ? (blend ? blend.targets[index].mask : 15) : 0;
        if (!written) {
            target.blend = undefined;
        }
        return target;
    });
    if (targets.length > 0) {
        descriptor.fragment = { module: pixelShader.module, entryPoint: pixelShader.entry, targets };
    } else if (pixelShader.bindings.length > 0 || pixelShader.outputs === 0) {
        descriptor.fragment = { module: pixelShader.module, entryPoint: pixelShader.entry, targets: [] };
    }
    if (attachments.depthFormat) {
        const d = depthState;
        const canStencil = attachments.depthFormat.includes('stencil');
        descriptor.depthStencil = {
            format: attachments.depthFormat,
            depthWriteEnabled: d ? !!(d.enable && d.write) : false,
            depthCompare: d && d.enable ? compareFunctions[d.compare] : 'always',
            depthBias: raster ? Math.round(raster.depthBias) : 0,
            depthBiasSlopeScale: raster ? raster.slope : 0,
        };
        if (canStencil && d && d.stencilEnable) {
            descriptor.depthStencil.stencilReadMask = d.stencilMask;
            descriptor.depthStencil.stencilWriteMask = d.stencilWriteMask;
            descriptor.depthStencil.stencilFront = stencilFace(d.stencilCompare, d.stencilFail, d.stencilDepthFail, d.stencilPass);
            descriptor.depthStencil.stencilBack = d.twoSided ? stencilFace(d.ccwCompare, d.ccwFail, d.ccwDepthFail, d.ccwPass) : descriptor.depthStencil.stencilFront;
        }
    }
    device.pushErrorScope('validation');
    pipeline = device.createRenderPipeline(descriptor);
    device.popErrorScope().then(error => {
        if (error) {
            report(`pipeline ${vertexShader.entry}/${pixelShader.entry} (${formats}, ${attachments.depthFormat}): ${error.message}`);
        }
    });
    stats.pipelines++;
    pipelineCache.set(key, pipeline);
    return pipeline;
}

function clampRect(x, y, width, height, maxWidth, maxHeight) {
    const left = Math.min(Math.max(x, 0), maxWidth);
    const top = Math.min(Math.max(y, 0), maxHeight);
    return [left, top, Math.max(0, Math.min(x + width, maxWidth) - left), Math.max(0, Math.min(y + height, maxHeight) - top)];
}

function prepareDraw(topology) {
    const renderPass = ensurePass();
    const attachments = state.attachments;
    if (state.groupsDirty[0] || state.groupsDirty[1]) {
        applyGroups(renderPass);
    }
    const pipeline = pipelineFor(topology, attachments);
    if (pipeline !== state.pipeline) {
        renderPass.setPipeline(pipeline);
        state.pipeline = pipeline;
    }
    if (state.groupsDirty[0] || state.passDirty) {
        renderPass.setBindGroup(0, state.groups[0].group, state.groups[0].dynamic);
        state.groupsDirty[0] = false;
    }
    if (state.groupsDirty[1] || state.passDirty) {
        renderPass.setBindGroup(1, state.groups[1].group, state.groups[1].dynamic);
        state.groupsDirty[1] = false;
    }
    if (state.passDirty) {
        renderPass.setBlendConstant({ r: state.blendConstant[0], g: state.blendConstant[1], b: state.blendConstant[2], a: state.blendConstant[3] });
        const d = depthStates.get(state.depth);
        if (d && d.stencilEnable) {
            renderPass.setStencilReference(d.reference);
        }
        state.passDirty = false;
    }
    if (state.vertexDirty) {
        for (let slot = 0; slot < state.vertexBuffers.length; slot++) {
            const binding = state.vertexBuffers[slot];
            const buffer = binding && buffers.get(binding.id);
            if (buffer) {
                markUsed(buffer);
                renderPass.setVertexBuffer(slot, buffer.gpu, binding.offset);
            }
        }
        state.vertexDirty = false;
    }
    if (state.viewportDirty) {
        const v = state.viewport;
        const [x, y, width, height] = clampRect(v[0], v[1], v[2], v[3], attachments.width, attachments.height);
        renderPass.setViewport(x, y, Math.max(width, 1), Math.max(height, 1), v[4], v[5]);
        state.viewportDirty = false;
    }
    if (state.scissorDirty) {
        const raster = rasterStates.get(state.raster);
        const s = state.scissor;
        const rect = raster && raster.scissor ? clampRect(s[0], s[1], s[2], s[3], attachments.width, attachments.height) : [0, 0, attachments.width, attachments.height];
        renderPass.setScissorRect(rect[0], rect[1], rect[2], rect[3]);
        state.scissorDirty = false;
    }
    return renderPass;
}

function indexCount(topology, primitives) {
    switch (topology) {
        case 0: return primitives * 3;
        case 1: return primitives + 2;
        case 2: return primitives * 2;
        case 3: return primitives + 1;
        default: return primitives;
    }
}

function bindIndex(renderPass) {
    const buffer = buffers.get(state.indexBuffer);
    if (state.indexDirty && buffer) {
        markUsed(buffer);
        renderPass.setIndexBuffer(buffer.gpu, state.indexFormat);
        state.indexDirty = false;
    }
}

// ---- Resources --------------------------------------------------------------------------------------------------

function bufferUsage(type) {
    switch (type) {
        case BufferIndex: return GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC;
        case BufferVertex: return GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC;
        default: return GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | GPUBufferUsage.COPY_SRC | GPUBufferUsage.INDIRECT;
    }
}

function newGpuBuffer(buffer) {
    return device.createBuffer({ size: Math.max((buffer.size + 3) & ~3, 4), usage: bufferUsage(buffer.type) });
}

// Uploads go through an ordinary copy, four-byte padded as writeBuffer requires.
function unsharedCopy(start, bytes) {
    const copy = new Uint8Array((bytes + 3) & ~3);
    copy.set(heap8.subarray(start, start + bytes));
    return copy;
}

function writeBufferData(gpu, offset, start, bytes) {
    if ((offset & 3) !== 0) {
        throw new Error('unaligned buffer write at ' + offset);
    }
    device.queue.writeBuffer(gpu, offset, unsharedCopy(start, bytes));
}

function setBufferData(id, offset, mode, start, bytes) {
    const buffer = buffers.get(id);
    if (!buffer) {
        return;
    }
    if (buffer.type === BufferConstant) {
        buffer.cpu.set(heap8.subarray(start, start + bytes), offset);
        buffer.arenaSerial = 0;
        for (let stage = 0; stage < 2; stage++) {
            if (state.cbuffers[stage].includes(id)) {
                state.groupsDirty[stage] = true;
            }
        }
        return;
    }
    if (buffer.usedSerial === encoderSerial && mode !== WriteNoOverwrite) {
        if (mode === WriteDiscard) {
            buffer.gpu = newGpuBuffer(buffer);
            buffer.generation++;
            state.vertexDirty = state.indexDirty = true;
        } else {
            flushEncoder();
        }
    }
    writeBufferData(buffer.gpu, offset, start, bytes);
}

function createTexture(id, flags, type, format, width, height, depth, mipmaps, slices, depthFormat, multiSample) {
    const info = surfaceFormats[format];
    if (!info) {
        throw new Error('unsupported surface format ' + format);
    }
    const isTarget = (flags & TextureFlagRenderTarget) !== 0;
    const depthSurface = format === DepthSurface;
    const sampleCount = isTarget && multiSample > 1 ? 4 : 1;
    let usage = GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.COPY_SRC;
    if (isTarget) {
        usage |= GPUTextureUsage.RENDER_ATTACHMENT;
    }
    if (flags & TextureFlagStorage) {
        usage |= GPUTextureUsage.STORAGE_BINDING;
    }
    const layers = type === Texture3D ? depth : type === TextureCube ? 6 : Math.max(slices, 1);
    const descriptor = { format: info[0], size: [width, height, layers], mipLevelCount: Math.max(mipmaps, 1), usage };
    if (type === Texture3D) {
        descriptor.dimension = '3d';
    }
    const unfilterable = depthSurface || (!adapterFeatures.has('float32-filterable') && /^(r|rg|rgba)32float/.test(info[0]));
    const texture = {
        id, flags, type, surface: format, format: info[0], blockBytes: info[1], blockSize: info[2], width, height, sampleCount, depthSurface, unfilterable,
        resolve: device.createTexture(descriptor), multisample: null, depthTexture: null, depthFormat: depthFormats[depthFormat],
        views: new Map(), sliceViews: new Map(), mipViews: new Map(), usedSerial: 0,
    };
    if (sampleCount > 1) {
        texture.multisample = device.createTexture({ format: info[0], size: [width, height, layers], sampleCount, usage: GPUTextureUsage.RENDER_ATTACHMENT });
    }
    if (isTarget && texture.depthFormat && !depthSurface) {
        texture.depthTexture = device.createTexture({ format: texture.depthFormat, size: [width, height], sampleCount, usage: GPUTextureUsage.RENDER_ATTACHMENT });
    }
    textures.set(id, texture);
}

function levelSize(size, level) {
    return Math.max(size >> level, 1);
}

async function readTexture(texture, level, slice, x, y, z, width, height, depth, pointer, bytes, flag) {
    const blockWidth = Math.ceil(width / texture.blockSize), blockHeight = Math.ceil(height / texture.blockSize);
    const rowBytes = blockWidth * texture.blockBytes;
    const paddedRow = (rowBytes + 255) & ~255;
    const staging = device.createBuffer({ size: paddedRow * blockHeight, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    const copy = device.createCommandEncoder();
    copy.copyTextureToBuffer({ texture: texture.resolve, mipLevel: level, origin: [x, y, texture.type === Texture3D ? z : slice], aspect: texture.depthSurface ? 'depth-only' : 'all' },
        { buffer: staging, bytesPerRow: paddedRow }, [width, height, 1]);
    device.queue.submit([copy.finish()]);
    await staging.mapAsync(GPUMapMode.READ);
    const mapped = new Uint8Array(staging.getMappedRange());
    refreshHeap();
    const target = heap8;
    for (let row = 0; row < blockHeight; row++) {
        const count = Math.min(rowBytes, bytes - row * rowBytes);
        if (count <= 0) {
            break;
        }
        target.set(mapped.subarray(row * paddedRow, row * paddedRow + count), pointer + row * rowBytes);
    }
    staging.unmap();
    staging.destroy();
    Atomics.store(heap32, flag >> 2, 1);
}

async function readBuffer(buffer, offset, pointer, bytes, flag) {
    const staging = device.createBuffer({ size: (bytes + 3) & ~3, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    const copy = device.createCommandEncoder();
    copy.copyBufferToBuffer(buffer.gpu, offset, staging, 0, (bytes + 3) & ~3);
    device.queue.submit([copy.finish()]);
    await staging.mapAsync(GPUMapMode.READ);
    refreshHeap();
    heap8.set(new Uint8Array(staging.getMappedRange(), 0, bytes), pointer);
    staging.unmap();
    staging.destroy();
    Atomics.store(heap32, flag >> 2, 1);
}

function setStageBinding(stage) {
    if (stage < 2) {
        state.groupsDirty[stage] = true;
    }
}

function resizeSwapchain(width, height, depth) {
    endPass();
    backWidth = width;
    backHeight = height;
    canvas.width = width;
    canvas.height = height;
    backBuffer = device.createTexture({ format: 'rgba8unorm', size: [width, height], usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC | GPUTextureUsage.TEXTURE_BINDING });
    backDepthFormat = depthFormats[depth];
    backDepth = backDepthFormat ? device.createTexture({ format: backDepthFormat, size: [width, height], usage: GPUTextureUsage.RENDER_ATTACHMENT }) : null;
}

let pendingShot = null;
let shotHandler = null;
// The next frame as a PNG, for the player.
export function takeScreenshot(handler) {
    shotHandler = handler;
    pendingShot = 'player';
}

let shotBase = '';
let onCapture = () => { };
export function requestScreenshot(name, log, base = '') {
    shotBase = base;
    pendingShot = name;
    onCapture = log || onCapture;
}

// Reads the frame back directly, so a capture shows what the game drew whatever the canvas does.
async function captureBackBuffer(name) {
    const padded = (backWidth * 4 + 255) & ~255;
    const staging = device.createBuffer({ size: padded * backHeight, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    const copy = device.createCommandEncoder();
    copy.copyTextureToBuffer({ texture: backBuffer }, { buffer: staging, bytesPerRow: padded }, [backWidth, backHeight]);
    device.queue.submit([copy.finish()]);
    await staging.mapAsync(GPUMapMode.READ);
    const mapped = new Uint8Array(staging.getMappedRange());
    const pixels = new Uint8ClampedArray(backWidth * backHeight * 4);
    let r = 0, g = 0, b = 0;
    for (let y = 0; y < backHeight; y++) {
        const row = mapped.subarray(y * padded, y * padded + backWidth * 4);
        pixels.set(row, y * backWidth * 4);
        for (let x = 0; x < backWidth * 4; x += 16) {
            r += row[x];
            g += row[x + 1];
            b += row[x + 2];
        }
    }
    staging.unmap();
    staging.destroy();
    const samples = backHeight * Math.floor(backWidth / 4);
    onCapture(`[Capture] ${name}: mean colour ${(r / samples).toFixed(1)}, ${(g / samples).toFixed(1)}, ${(b / samples).toFixed(1)}`);
    for (let i = 3; i < pixels.length; i += 4) {
        pixels[i] = 255;
    }
    const surface = new OffscreenCanvas(backWidth, backHeight);
    surface.getContext('2d').putImageData(new ImageData(pixels, backWidth, backHeight), 0, 0);
    const png = await surface.convertToBlob({ type: 'image/png' });
    if (shotHandler) {
        const handler = shotHandler;
        shotHandler = null;
        handler(png);
        return;
    }
    await fetch(`${shotBase}/shot/${name}`, { method: 'POST', mode: shotBase ? 'no-cors' : 'cors', body: shotBase ? new Blob([png]) : png });
}

function present() {
    endPass();
    ensureEncoder();
    if (backBuffer && canvas.width === backWidth && canvas.height === backHeight) {
        encoder.copyTextureToTexture({ texture: backBuffer }, { texture: context.getCurrentTexture() }, [backWidth, backHeight]);
    }
    flushEncoder();
    stats.frames++;
    const submitted = performance.now();
    device.queue.onSubmittedWorkDone().then(() => {
        stats.gpuWaitMs += performance.now() - submitted;
        if (uncapped) {
            Atomics.add(control, FramesAcknowledged, 1);
        }
    });
    framePresented = !uncapped;
    if (pendingShot) {
        const name = pendingShot;
        pendingShot = null;
        captureBackBuffer(name).catch(report);
    }
}

// ---- The replay -------------------------------------------------------------------------------------------------

function replay(pointer, words) {
    refreshHeap();
    const w = heap32, f = heapF32;
    let i = pointer >> 2;
    const end = i + words;
    while (i < end) {
        const op = w[i++];
        switch (op) {
            case Op.ResizeSwapchain:
                resizeSwapchain(w[i], w[i + 1], w[i + 2]);
                i += 3;
                break;
            case Op.BeginFrame:
                break;
            case Op.Clear: {
                endPass();
                state.pendingClear = { options: w[i], color: [f[i + 1], f[i + 2], f[i + 3], f[i + 4]], depth: f[i + 5], stencil: w[i + 6] };
                i += 7;
                break;
            }
            case Op.Present:
                present();
                break;
            case Op.SetBlendState:
                state.blend = w[i];
                state.blendConstant = [f[i + 1], f[i + 2], f[i + 3], f[i + 4]];
                state.passDirty = true;
                i += 5;
                break;
            case Op.SetDepthStencilState:
                state.depth = w[i++];
                state.passDirty = true;
                break;
            case Op.SetRasterizerState:
                state.raster = w[i++];
                state.scissorDirty = true;
                break;
            case Op.SetViewport:
                state.viewport = [w[i], w[i + 1], w[i + 2], w[i + 3], f[i + 4], f[i + 5]];
                state.viewportDirty = true;
                i += 6;
                break;
            case Op.SetScissor:
                state.scissor = [w[i], w[i + 1], w[i + 2], w[i + 3]];
                state.scissorDirty = true;
                i += 4;
                break;
            case Op.SetRenderTargets: {
                endPass();
                const count = w[i++];
                state.targets = [];
                for (let t = 0; t < count; t++) {
                    state.targets.push({ id: w[i], slice: w[i + 1] });
                    i += 2;
                }
                break;
            }
            case Op.SetConstantBuffer:
                state.cbuffers[w[i]][w[i + 1]] = w[i + 2];
                setStageBinding(w[i]);
                i += 3;
                break;
            case Op.SetTexture:
                if (state.textures[w[i]][w[i + 1]] !== w[i + 2]) {
                    state.textures[w[i]][w[i + 1]] = w[i + 2];
                    setStageBinding(w[i]);
                }
                i += 3;
                break;
            case Op.SetSampler:
                if (state.samplers[w[i]][w[i + 1]] !== w[i + 2]) {
                    state.samplers[w[i]][w[i + 1]] = w[i + 2];
                    setStageBinding(w[i]);
                }
                i += 3;
                break;
            case Op.SetIndexBuffer:
                state.indexFormat = w[i] === 0 ? 'uint16' : 'uint32';
                state.indexBuffer = w[i + 1];
                state.indexDirty = true;
                i += 2;
                break;
            case Op.SetVertexBuffer:
                state.vertexBuffers[w[i]] = { id: w[i + 1], offset: w[i + 2] };
                state.vertexDirty = true;
                i += 3;
                break;
            case Op.SetShader:
                state.shader[w[i]] = w[i + 1];
                setStageBinding(w[i]);
                i += 2;
                break;
            case Op.SetInputLayout:
                state.layout = w[i++];
                break;
            case Op.Draw: {
                const renderPass = prepareDraw(w[i]);
                renderPass.draw(w[i + 2], 1, w[i + 1], 0);
                stats.draws++;
                i += 3;
                break;
            }
            case Op.DrawIndexed: {
                const renderPass = prepareDraw(w[i]);
                bindIndex(renderPass);
                renderPass.drawIndexed(indexCount(w[i], w[i + 1]), 1, w[i + 2], w[i + 3], 0);
                stats.draws++;
                i += 4;
                break;
            }
            case Op.DrawIndexedInstanced: {
                const renderPass = prepareDraw(w[i]);
                bindIndex(renderPass);
                renderPass.drawIndexed(indexCount(w[i], w[i + 1]), w[i + 5], w[i + 2], w[i + 3], w[i + 4]);
                stats.draws++;
                i += 6;
                break;
            }
            case Op.Resolve:
                endPass();
                break;
            case Op.CreateBlendState: {
                const targets = [];
                const id = w[i++];
                for (let t = 0; t < 4; t++) {
                    targets.push({ colorSource: w[i], colorDest: w[i + 1], colorFunction: w[i + 2], alphaSource: w[i + 3], alphaDest: w[i + 4], alphaFunction: w[i + 5], mask: w[i + 6] & 15 });
                    i += 7;
                }
                blendStates.set(id, { targets });
                break;
            }
            case Op.CreateDepthStencilState:
                depthStates.set(w[i], {
                    enable: w[i + 1], write: w[i + 2], compare: w[i + 3], reference: w[i + 4], stencilEnable: w[i + 5], stencilMask: w[i + 6], stencilWriteMask: w[i + 7],
                    stencilCompare: w[i + 8], stencilDepthFail: w[i + 9], stencilFail: w[i + 10], stencilPass: w[i + 11], twoSided: w[i + 12],
                    ccwCompare: w[i + 13], ccwDepthFail: w[i + 14], ccwFail: w[i + 15], ccwPass: w[i + 16],
                });
                i += 17;
                break;
            case Op.CreateRasterizerState:
                rasterStates.set(w[i], { fill: w[i + 1], cull: w[i + 2], scissor: w[i + 3], depthClip: w[i + 4], depthBias: f[i + 5], slope: f[i + 6] });
                i += 7;
                break;
            case Op.CreateSampler: {
                const filter = w[i + 4];
                const comparison = w[i + 5] === 1;
                // Linear, Point, Anisotropic, LinearMipPoint, PointMipLinear, MinLinearMagPointMipLinear, MinLinearMagPointMipPoint, MinPointMagLinearMipLinear, MinPointMagLinearMipPoint
                const min = [1, 0, 1, 1, 0, 1, 1, 0, 0][filter], mag = [1, 0, 1, 1, 0, 0, 0, 1, 1][filter], mip = [1, 0, 1, 0, 1, 1, 0, 1, 0][filter];
                const descriptor = {
                    addressModeU: addressModes[w[i + 1]], addressModeV: addressModes[w[i + 2]], addressModeW: addressModes[w[i + 3]],
                    magFilter: mag ? 'linear' : 'nearest', minFilter: min ? 'linear' : 'nearest', mipmapFilter: mip ? 'linear' : 'nearest',
                    lodMinClamp: Math.max(w[i + 7], 0),
                };
                if (filter === 2 && w[i + 6] > 1) {
                    descriptor.maxAnisotropy = w[i + 6];
                }
                samplers.set(w[i], {
                    id: w[i], filtering: !!(min || mag || mip), gpu: device.createSampler(descriptor),
                    comparison: device.createSampler({ ...descriptor, compare: compareFunctions[w[i + 8]] === 'always' && !comparison ? 'less-equal' : compareFunctions[w[i + 8]], maxAnisotropy: undefined }),
                });
                i += 9;
                break;
            }
            case Op.CreateBuffer: {
                const buffer = { id: w[i], type: w[i + 1], size: w[i + 3], gpu: null, cpu: null, usedSerial: 0, generation: 0, arenaSerial: 0, arenaOffset: 0 };
                if (buffer.type === BufferConstant) {
                    buffer.size = (buffer.size + 15) & ~15;
                    buffer.cpu = new Uint8Array(buffer.size);
                } else {
                    buffer.gpu = newGpuBuffer(buffer);
                }
                buffers.set(buffer.id, buffer);
                i += 4;
                break;
            }
            case Op.DestroyBuffer: {
                const buffer = buffers.get(w[i++]);
                if (buffer) {
                    buffers.delete(buffer.id);
                }
                break;
            }
            case Op.BufferSetData: {
                const bytes = w[i + 3];
                setBufferData(w[i], w[i + 1], w[i + 2], (i + 4) << 2, bytes);
                i += 4 + ((bytes + 3) >> 2);
                break;
            }
            case Op.CreateTexture:
                createTexture(w[i], w[i + 1], w[i + 2], w[i + 3], w[i + 4], w[i + 5], w[i + 6], w[i + 7], w[i + 8], w[i + 9], w[i + 10]);
                i += 11;
                break;
            case Op.DestroyTexture: {
                const texture = textures.get(w[i++]);
                if (texture) {
                    textures.delete(texture.id);
                    destroyAfterSubmit.push(texture.resolve);
                    if (texture.multisample) {
                        destroyAfterSubmit.push(texture.multisample);
                    }
                    if (texture.depthTexture) {
                        destroyAfterSubmit.push(texture.depthTexture);
                    }
                }
                break;
            }
            case Op.TextureSetData: {
                const texture = textures.get(w[i]) || { width: 0, height: 0 };
                const level = w[i + 1], slice = w[i + 2], x = w[i + 3], y = w[i + 4], z = w[i + 5], bytes = w[i + 9];
                // A size of zero means the whole mip level.
                const width = w[i + 6] || levelSize(texture.width, level), height = w[i + 7] || levelSize(texture.height, level), depth = w[i + 8] || 1;
                const start = (i + 10) << 2;
                if (texture.resolve) {
                    if (texture.usedSerial === encoderSerial) {
                        flushEncoder();
                    }
                    const blockWidth = Math.ceil(width / texture.blockSize), blockHeight = Math.ceil(height / texture.blockSize);
                    device.queue.writeTexture(
                        { texture: texture.resolve, mipLevel: level, origin: [x, y, texture.type === Texture3D ? z : slice] },
                        unsharedCopy(start, bytes),
                        { bytesPerRow: blockWidth * texture.blockBytes, rowsPerImage: blockHeight },
                        [width, height, Math.max(depth, 1)]);
                }
                i += 10 + ((bytes + 3) >> 2);
                break;
            }
            case Op.TextureGetData: {
                flushEncoder();
                const texture = textures.get(w[i]);
                readTexture(texture, w[i + 1], w[i + 2], w[i + 3], w[i + 4], w[i + 5], w[i + 6] || levelSize(texture.width, w[i + 1]), w[i + 7] || levelSize(texture.height, w[i + 1]), w[i + 8] || 1, w[i + 9], w[i + 10], w[i + 11]).catch(report);
                i += 12;
                break;
            }
            case Op.BufferGetData: {
                flushEncoder();
                readBuffer(buffers.get(w[i]), w[i + 1], w[i + 2], w[i + 3], w[i + 4]).catch(report);
                i += 5;
                break;
            }
            case Op.BackBufferGetData: {
                flushEncoder();
                readTexture({ resolve: backBuffer, blockSize: 1, blockBytes: 4, type: 0 }, 0, 0, w[i], w[i + 1], 0, w[i + 2], w[i + 3], 1, w[i + 4], w[i + 5], w[i + 6]).catch(report);
                i += 7;
                break;
            }
            case Op.CreateInputLayout: {
                const id = w[i++];
                i++;
                const streamCount = w[i++];
                const strides = [];
                for (let s = 0; s < streamCount; s++) {
                    strides.push(w[i++]);
                }
                const elementCount = w[i++];
                const elements = [];
                for (let e = 0; e < elementCount; e++) {
                    elements.push({ usage: w[i], index: w[i + 1], slot: w[i + 2], format: w[i + 3], offset: w[i + 4], stepRate: w[i + 5] });
                    i += 6;
                }
                inputLayouts.set(id, { strides, elements });
                break;
            }
            case Op.DestroyInputLayout:
                inputLayouts.delete(w[i++]);
                break;
            case Op.CreateShader: {
                const bytes = w[i + 2];
                const start = (i + 3) << 2;
                const code = new TextDecoder().decode(heap8.slice(start, start + bytes));
                shaders.set(w[i], parseShader(code, w[i + 1]));
                i += 3 + ((bytes + 3) >> 2);
                break;
            }
            case Op.DestroyShader:
                shaders.delete(w[i++]);
                break;
            case Op.Dispatch:
                dispatch(w[i], w[i + 1], w[i + 2], null, 0);
                i += 3;
                break;
            case Op.DispatchIndirect:
                dispatch(0, 0, 0, buffers.get(w[i]), w[i + 1]);
                i += 2;
                break;
            case Op.SetShaderBuffer: {
                const stage = w[i], slot = w[i + 1], writable = w[i + 3];
                state.storage[stage].set(writable ? BindingWritable + slot : BindingTexture + slot, w[i + 2]);
                setStageBinding(stage);
                i += 4;
                break;
            }
            case Op.SetShaderImage:
                state.storage[w[i]].set(BindingWritable + w[i + 1], { texture: w[i + 2], mip: w[i + 3] });
                setStageBinding(w[i]);
                i += 4;
                break;
            case Op.DestroyState: {
                const id = w[i++];
                blendStates.delete(id);
                depthStates.delete(id);
                rasterStates.delete(id);
                samplers.delete(id);
                break;
            }
            default:
                throw new Error(`unknown graphics operation ${op} at word ${i - 1 - (pointer >> 2)} of ${words}`);
        }
    }
}

const destroyAfterSubmit = [];

function dispatch(x, y, z, indirect, offset) {
    endPass();
    ensureEncoder();
    const shader = shaders.get(state.shader[StageCompute]);
    const resolved = resolveGroup(StageCompute, 0, shader);
    const key = `${state.shader[StageCompute]}|${resolved.layout.key}`;
    let pipeline = computePipelines.get(key);
    if (!pipeline) {
        pipeline = device.createComputePipeline({ layout: device.createPipelineLayout({ bindGroupLayouts: [resolved.layout] }), compute: { module: shader.module, entryPoint: shader.entry } });
        computePipelines.set(key, pipeline);
    }
    const computePass = encoder.beginComputePass();
    computePass.setPipeline(pipeline);
    computePass.setBindGroup(0, resolved.group, resolved.dynamic);
    if (indirect) {
        markUsed(indirect);
        computePass.dispatchWorkgroupsIndirect(indirect.gpu, offset);
    } else {
        computePass.dispatchWorkgroups(x, y, z);
    }
    computePass.end();
    stats.dispatches++;
}

let framePresented = false;
let uncapped = false;
// Measurement: frames as fast as the GPU finishes them, instead of one per display refresh.
export function setUncapped(value) {
    uncapped = value;
}

function pump() {
    const started = performance.now();
    try {
        let head = Atomics.load(control, QueueHead);
        let tail = Atomics.load(control, QueueTail);
        while (tail !== head) {
            const slot = tail % QueueLength;
            replay(control[QueueBase + slot * 2], control[QueueBase + slot * 2 + 1]);
            stats.chunks++;
            tail++;
            Atomics.store(control, QueueTail, tail);
            head = Atomics.load(control, QueueHead);
        }
    } catch (error) {
        report('replay: ' + (error && error.stack || error));
        return;
    }
    stats.replayMs += performance.now() - started;
    setTimeout(pump, 0);
}

function acknowledgeLoop() {
    if (framePresented) {
        framePresented = false;
        Atomics.add(control, FramesAcknowledged, 1);
        while (destroyAfterSubmit.length > 0) {
            destroyAfterSubmit.pop().destroy();
        }
    }
    requestAnimationFrame(acknowledgeLoop);
}

// ---- Input ------------------------------------------------------------------------------------------------------

const EventType = { WindowResized: 2, WindowGainedFocus: 3, WindowLostFocus: 4, KeyDown: 6, KeyUp: 7, TextInput: 8, MouseMove: 9, MouseDown: 10, MouseUp: 11, MouseWheel: 12 };
const WheelNotch = 120;

const codeToKey = {
    Backspace: 8, Tab: 9, Enter: 13, Pause: 19, CapsLock: 20, Escape: 27, Space: 32, PageUp: 33, PageDown: 34, End: 35, Home: 36, ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40,
    PrintScreen: 44, Insert: 45, Delete: 46, MetaLeft: 91, MetaRight: 92, ContextMenu: 93, NumpadMultiply: 106, NumpadAdd: 107, NumpadSubtract: 109, NumpadDecimal: 110, NumpadDivide: 111,
    NumLock: 144, ScrollLock: 145, ShiftLeft: 160, ShiftRight: 161, ControlLeft: 162, ControlRight: 163, AltLeft: 164, AltRight: 165, Semicolon: 186, Equal: 187, Comma: 188, Minus: 189,
    Period: 190, Slash: 191, Backquote: 192, BracketLeft: 219, Backslash: 220, BracketRight: 221, Quote: 222, NumpadEnter: 13,
};

function keyCode(code) {
    if (codeToKey[code] !== undefined) {
        return codeToKey[code];
    }
    let m = code.match(/^Key([A-Z])$/);
    if (m) {
        return m[1].charCodeAt(0);
    }
    m = code.match(/^Digit(\d)$/);
    if (m) {
        return 48 + +m[1];
    }
    m = code.match(/^Numpad(\d)$/);
    if (m) {
        return 96 + +m[1];
    }
    m = code.match(/^F(\d+)$/);
    if (m) {
        return 111 + +m[1];
    }
    return 0;
}

// Off while a panel of the site is open: its typing and clicks are not the game's.
let inputEnabled = true;
export function setInputEnabled(value) {
    inputEnabled = value;
}

function pushEvent(type, a = 0, b = 0, c = 0, d = 0) {
    const head = Atomics.load(control, EventHead);
    if (head - Atomics.load(control, EventTail) >= EventCapacity) {
        return;
    }
    const at = EventBase + (head % EventCapacity) * EventWords;
    control[at] = type;
    control[at + 1] = a;
    control[at + 2] = b;
    control[at + 3] = c;
    control[at + 4] = d;
    Atomics.store(control, EventHead, head + 1);
}

function canvasPoint(event) {
    const rect = canvas.getBoundingClientRect();
    return [Math.round((event.clientX - rect.left) * canvas.width / rect.width), Math.round((event.clientY - rect.top) * canvas.height / rect.height)];
}

function installInput() {
    canvas.tabIndex = 0;
    canvas.addEventListener('contextmenu', event => event.preventDefault());
    canvas.addEventListener('mousemove', event => {
        if (!inputEnabled) {
            return;
        }
        const [x, y] = canvasPoint(event);
        pushEvent(EventType.MouseMove, x, y);
    });
    const buttons = [0, 1, 2, 3, 4];
    canvas.addEventListener('mousedown', event => {
        canvas.focus();
        const [x, y] = canvasPoint(event);
        pushEvent(EventType.MouseDown, buttons[event.button] ?? 0, x, y);
        event.preventDefault();
    });
    window.addEventListener('mouseup', event => {
        const [x, y] = canvasPoint(event);
        pushEvent(EventType.MouseUp, buttons[event.button] ?? 0, x, y);
    });
    canvas.addEventListener('wheel', event => {
        pushEvent(EventType.MouseWheel, -Math.sign(event.deltaY) * WheelNotch, Math.sign(event.deltaX) * WheelNotch);
        event.preventDefault();
    }, { passive: false });
    window.addEventListener('keydown', event => {
        if (!inputEnabled) {
            return;
        }
        const key = keyCode(event.code);
        const character = event.key.length === 1 ? event.key.codePointAt(0) : event.key === 'Backspace' ? 8 : event.key === 'Enter' ? 13 : event.key === 'Tab' ? 9 : 0;
        pushEvent(EventType.KeyDown, key, character);
        if (event.key.length === 1 && !event.ctrlKey && !event.metaKey) {
            pushEvent(EventType.TextInput, key, character);
        }
        if (['Tab', 'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10', 'F11', 'F12', 'Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Backspace'].includes(event.code)) {
            event.preventDefault();
        }
    });
    window.addEventListener('keyup', event => inputEnabled && pushEvent(EventType.KeyUp, keyCode(event.code), 0));
    window.addEventListener('focus', () => pushEvent(EventType.WindowGainedFocus));
    window.addEventListener('blur', () => pushEvent(EventType.WindowLostFocus));
}

// ---- Start ------------------------------------------------------------------------------------------------------

// Before .NET starts: the adapter and device.
export async function prepareGpu(target, errorHandler) {
    canvas = target;
    onError = errorHandler;
    if (!navigator.gpu) {
        throw new Error('This browser has no WebGPU.');
    }
    const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
    if (!adapter) {
        throw new Error('This browser has WebGPU but no usable graphics adapter.');
    }
    adapterFeatures = adapter.features;
    const wanted = ['float32-filterable', 'float32-blendable', 'rg11b10ufloat-renderable', 'texture-compression-bc', 'depth-clip-control', 'indirect-first-instance', 'depth32float-stencil8']
        .filter(name => adapter.features.has(name));
    const limits = {};
    for (const name of ['maxStorageBuffersPerShaderStage', 'maxSampledTexturesPerShaderStage', 'maxSamplersPerShaderStage', 'maxUniformBufferBindingSize', 'maxBindingsPerBindGroup', 'maxColorAttachmentBytesPerSample', 'maxTextureDimension2D', 'maxBufferSize', 'maxStorageBufferBindingSize', 'maxDynamicUniformBuffersPerPipelineLayout', 'maxComputeInvocationsPerWorkgroup']) {
        limits[name] = adapter.limits[name];
    }
    device = await adapter.requestDevice({ requiredFeatures: wanted, requiredLimits: limits });
    device.lost.then(info => report('GPU device lost: ' + info.message));
    device.addEventListener('uncapturederror', event => report('GPU: ' + event.error.message));
    context = canvas.getContext('webgpu');
    context.configure({ device, format: 'rgba8unorm', alphaMode: 'opaque', usage: GPUTextureUsage.COPY_DST | GPUTextureUsage.RENDER_ATTACHMENT });
    nearestSampler = device.createSampler();
    uniformArena = device.createBuffer({ size: UniformArenaBytes, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    uniformCpu = new Uint8Array(UniformArenaBytes);
    return adapter.info;
}

export function imports(runtimeModule) {
    module_ = runtimeModule;
    return {
        startAudio(pointer) {
            startAudioRuntime(module_, pointer);
        },
        startRuntime(pointer) {
            refreshHeap();
            control = new Int32Array(module_.HEAPU8.buffer, pointer, ControlWords);
            installInput();
            pushEvent(EventType.WindowGainedFocus);
            pump();
            requestAnimationFrame(acknowledgeLoop);
        },
    };
}

export function runtimeStats() {
    return { ...stats };
}
