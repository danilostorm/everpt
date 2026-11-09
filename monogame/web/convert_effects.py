"""Converts compiled effects for the browser build: the SPIR-V of every shader in an MGFX effect becomes WGSL text.

The rest of the effect (parameters, techniques, reflection) stays as the fork's effect compiler wrote it, so the framework
loads the same files; Shader_Create receives WGSL instead of SPIR-V. See monogame/web/README.md.

Usage: python convert_effects.py <naga> <content dir> <output dir> <runtime vulkan dir> <dxc> <shader sources dir>
  Copies the content to the output with its effects converted, and writes BuiltinEffects/<Name>.mgfxo from the effects the
  native runtime embeds (<runtime vulkan dir>/*.vk.mgfxo.h).
"""
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile

SAMPLER_SHIFT = 96     # WebGPU cannot share a binding number between a texture and a sampler as the Vulkan effects do
OP_NAME = 5
OP_DECORATE = 71
OP_TYPE_SAMPLER = 26
OP_TYPE_POINTER = 32
OP_VARIABLE = 59
DECORATION_BINDING = 33
SPIRV_MAGIC = 0x07230203


def read_7bit(data, at):
    value = shift = 0
    while True:
        byte = data[at]
        at += 1
        value |= (byte & 0x7F) << shift
        shift += 7
        if not byte & 0x80:
            return value, at


def write_7bit(value):
    out = bytearray()
    while value >= 0x80:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    out.append(value)
    return bytes(out)


def skip_string(data, at):
    length, at = read_7bit(data, at)
    return at + length


def read_string(data, at):
    length, at = read_7bit(data, at)
    return data[at:at + length].decode('utf-8'), at + length


def separate_sampler_bindings(bytecode):
    # The Vulkan effects carry their descriptor table in front of the SPIR-V; the web runtime reads bindings from the WGSL.
    start = bytecode.find(struct.pack('<I', SPIRV_MAGIC))
    if start < 0 or start % 4:
        raise ValueError('not SPIR-V')
    spirv = bytecode[start:]
    words = list(struct.unpack('<%dI' % (len(spirv) // 4), spirv))
    sampler_types = set()
    sampler_pointers = set()
    sampler_variables = set()
    i = 5
    while i < len(words):
        op = words[i] & 0xFFFF
        count = words[i] >> 16
        if op == OP_TYPE_SAMPLER:
            sampler_types.add(words[i + 1])
        elif op == OP_TYPE_POINTER and words[i + 3] in sampler_types:
            sampler_pointers.add(words[i + 1])
        elif op == OP_VARIABLE and words[i + 1] in sampler_pointers:
            sampler_variables.add(words[i + 2])
        i += count
    i = 5
    while i < len(words):
        op = words[i] & 0xFFFF
        count = words[i] >> 16
        if op == OP_DECORATE and words[i + 2] == DECORATION_BINDING and words[i + 1] in sampler_variables:
            words[i + 3] += SAMPLER_SHIFT
        i += count
    return struct.pack('<%dI' % len(words), *words)


class Converter:
    def __init__(self, naga, dxc, sources):
        self.naga = naga
        self.dxc = dxc
        self.sources = sources
        self.cache = {}
        self.fallbacks = 0

    def naga_wgsl(self, spirv, folder):
        source = os.path.join(folder, 'in.spv')
        target = os.path.join(folder, 'out.wgsl')
        with open(source, 'wb') as out:
            out.write(spirv)
        result = subprocess.run([self.naga, source, target], capture_output=True, text=True, errors='replace')
        if result.returncode:
            return None, result.stderr.strip()
        return open(target, encoding='utf-8').read(), None

    def source_path(self, effect):
        return os.path.join(self.sources, 'Passes', effect.replace('StaticSurface', 'Surface').replace('SkinnedSurface', 'Surface') + '.fx')

    # A shape of DXC's optimized SPIR-V naga cannot read: the same shader without DXC's optimizer; the browser optimizes it.
    def unoptimized(self, effect, profile, entry, folder):
        text = open(self.source_path(effect), encoding='utf-8').read()
        text = re.sub(r'\btechnique\s+\w+\s*\{.*?\}\s*\}\s*', '', text, flags=re.S)
        text = re.sub(r'=\s*sampler_state\s*\{[^}]*\}', '', text)
        text = re.sub(r'#include\s+"([^"]+)"', lambda m: '#include "%s"' % os.path.normpath(os.path.join(self.sources, 'Passes', m.group(1))).replace('\\', '/'), text)
        hlsl = os.path.join(folder, 'in.hlsl')
        with open(hlsl, 'w', encoding='utf-8') as out:
            out.write(text)
        spv = os.path.join(folder, 'o0.spv')
        args = [self.dxc, '-nologo', '-spirv', '-fvk-use-dx-layout', '-O0', '-T', profile, '-E', entry, '-fvk-t-shift', '32', 'all', '-fvk-s-shift', '32', 'all', '-fvk-u-shift', '64', 'all']
        args += ['-auto-binding-space', '1'] if profile.startswith('ps') else ['-fvk-invert-y', '-fvk-use-dx-position-w'] if profile.startswith('vs') else []
        args += ['-Fo', spv, hlsl]
        subprocess.run(args, capture_output=True, check=True)
        return open(spv, 'rb').read()

    def wgsl(self, spirv, effect, stage, entry):
        if spirv in self.cache:
            return self.cache[spirv]
        with tempfile.TemporaryDirectory() as folder:
            text, error = self.naga_wgsl(separate_sampler_bindings(spirv), folder)
            if text is None and self.dxc and os.path.exists(self.source_path(effect)):
                profile = {0: 'vs_6_0', 1: 'ps_6_0', 2: 'cs_6_0'}[stage]
                text, error = self.naga_wgsl(separate_sampler_bindings(self.unoptimized(effect, profile, entry, folder)), folder)
                self.fallbacks += 1
            if text is None:
                raise RuntimeError('%s %s: %s' % (effect, entry, error))
        self.cache[spirv] = text.encode('utf-8')
        return self.cache[spirv]

    def convert_effect(self, data, effect):
        """Returns the MGFX effect with its shaders converted; data starts at the MGFX signature."""
        version = data[4]
        at = 10
        count = struct.unpack_from('<i', data, at)[0]
        at += 4
        for _ in range(count):
            at = skip_string(data, at)
            at += 2
            parameters = struct.unpack_from('<i', data, at)[0]
            at += 4 + parameters * 6
        shaders = struct.unpack_from('<i', data, at)[0]
        at += 4
        out = bytearray(data[:at])
        for _ in range(shaders):
            stage = data[at] if version >= 12 else (0 if data[at] else 1)
            at += 1
            _, after = read_string(data, at)
            entry, after = read_string(data, after)
            out += data[at - 1:after]
            at = after
            length = struct.unpack_from('<i', data, at)[0]
            at += 4
            spirv = bytes(data[at:at + length])
            at += length
            wgsl = self.wgsl(spirv, effect, stage, entry)
            out += struct.pack('<i', len(wgsl)) + wgsl
            at_after_shader = self.skip_shader_tail(data, at, version)
            out += data[at:at_after_shader]
            at = at_after_shader
        out += data[at:]
        return bytes(out)

    @staticmethod
    def skip_shader_tail(data, at, version):
        samplers = data[at]
        at += 1
        for _ in range(samplers):
            at += 3
            has_state = data[at]
            at += 1
            if has_state:
                at += 3 + 4 + 1 + 4 + 4 + 4
            at = skip_string(data, at)
            at += 1
        cbuffers = data[at]
        at += 1 + cbuffers
        attributes = data[at]
        at += 1
        for _ in range(attributes):
            at = skip_string(data, at)
            at += 1 + 1 + 2
        if version < 12:
            return at
        storage = data[at]
        at += 1
        for _ in range(storage):
            at += 1 + 1 + 4 + 4
            at = skip_string(data, at)
            at += 4
        at += 4 + 4 + 4 + 4
        return at


def convert_xnb(converter, path, target, effect):
    data = open(path, 'rb').read()
    at = data.find(b'MGFX')
    if at < 0:
        shutil.copy(path, target)
        return False
    length = struct.unpack_from('<i', data, at - 4)[0]
    converted = converter.convert_effect(data[at:at + length], effect)
    out = bytearray(data[:at - 4]) + struct.pack('<i', len(converted)) + converted + data[at + length:]
    struct.pack_into('<i', out, 6, len(out))
    with open(target, 'wb') as output:
        output.write(out)
    return True


def builtin_effects(runtime):
    for name in sorted(os.listdir(runtime)):
        match = re.match(r'(\w+)\.vk\.mgfxo\.h$', name)
        if match:
            text = open(os.path.join(runtime, name), encoding='utf-8', errors='replace').read()
            body = text[text.index('{') + 1:text.rindex('}')]
            yield match.group(1), bytes(int(value, 0) for value in re.findall(r'0x[0-9a-fA-F]+|\b\d+\b', body))


def main():
    naga, content, output, runtime, dxc, sources = sys.argv[1:7]
    converter = Converter(naga, dxc, sources)
    converted = 0
    for folder, _, files in os.walk(content):
        relative = os.path.relpath(folder, content)
        os.makedirs(os.path.join(output, relative), exist_ok=True)
        for name in files:
            if name.endswith('.xnb'):
                if convert_xnb(converter, os.path.join(folder, name), os.path.join(output, relative, name), name[:-4]):
                    converted += 1
            elif not name.endswith('.mgcontent'):
                shutil.copy(os.path.join(folder, name), os.path.join(output, relative, name))
    builtin = os.path.join(output, 'BuiltinEffects')
    os.makedirs(builtin, exist_ok=True)
    for name, data in builtin_effects(runtime):
        try:
            effect = converter.convert_effect(data, name)
        except RuntimeError as error:
            print('builtin effect %s not converted: %s' % (name, str(error)[:400]))
            continue
        with open(os.path.join(builtin, name + '.mgfxo'), 'wb') as out:
            out.write(effect)
        converted += 1
    print('%d effects converted (%d distinct shaders, %d through the unoptimized path) -> %s' % (converted, len(converter.cache), converter.fallbacks, output))


if __name__ == '__main__':
    main()
