// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: compute dispatches and the storage resources of every stage (monogame/issues/005-compute-and-uav.md,
// 006-structured-buffers.md).

using System;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Graphics
{
    public partial class GraphicsDevice
    {
        private const int StorageReadSlots = 16;    // t registers
        private const int StorageWriteSlots = 8;    // u registers (MGVK_MAX_UAV_SLOTS)

        // A storage resource bound to a register of a stage, and whether the runtime has it.
        private struct StorageBinding
        {
            public GraphicsResource Resource;
            public int Mip;
            public bool Writable;
        }

        private Shader _computeShader;
        private bool _computeShaderDirty;
        private readonly ConstantBufferCollection _computeConstantBuffers = new ConstantBufferCollection(ShaderStage.Compute, 16);
        private readonly StorageBinding[][] _readStorage = NewStorageTable(StorageReadSlots);
        private readonly StorageBinding[][] _writeStorage = NewStorageTable(StorageWriteSlots);
        private readonly uint[] _storageDirty = new uint[3];   // per stage: bits 0-15 t-slots, 16-23 u-slots

        private static StorageBinding[][] NewStorageTable(int slots)
        {
            var table = new StorageBinding[3][];
            for (var i = 0; i < table.Length; i++)
                table[i] = new StorageBinding[slots];
            return table;
        }

        /// <summary>The textures the compute shader samples (t registers), set by a compute <see cref="EffectPass"/>.</summary>
        public TextureCollection ComputeTextures { get; private set; }

        /// <summary>The samplers of <see cref="ComputeTextures"/>.</summary>
        public SamplerStateCollection ComputeSamplerStates { get; private set; }

        internal Shader ComputeShader
        {
            get { return _computeShader; }

            set
            {
                if (_computeShader == value)
                    return;

                _computeShader = value;
                _computeConstantBuffers.Clear();
                _computeShaderDirty = true;
            }
        }

        /// <summary>
        /// Runs the compute shader of the last applied compute <see cref="EffectPass"/> over
        /// <paramref name="groupsX"/> × <paramref name="groupsY"/> × <paramref name="groupsZ"/> thread groups, in the
        /// frame's own command buffer: it sees everything drawn and dispatched before it in the frame, and everything
        /// after it sees what it wrote (storage images, buffers, indirect arguments). It ends the current render pass;
        /// the next draw starts a new one that keeps the targets' content.
        /// </summary>
        /// <exception cref="InvalidOperationException">No compute pass was applied.</exception>
        public unsafe void Dispatch(int groupsX, int groupsY, int groupsZ)
        {
            if (groupsX <= 0 || groupsY <= 0 || groupsZ <= 0)
                throw new ArgumentOutOfRangeException(nameof(groupsX), "A dispatch runs at least one thread group in each dimension.");
            ApplyComputeState();
            MGG.GraphicsDevice_Dispatch(Handle, groupsX, groupsY, groupsZ);
        }

        /// <summary>
        /// Like <see cref="Dispatch"/>, with the group counts read by the GPU from three <c>uint</c>s at
        /// <paramref name="byteOffset"/> in <paramref name="arguments"/> (typically written by an earlier dispatch).
        /// </summary>
        /// <exception cref="InvalidOperationException">No compute pass was applied.</exception>
        public unsafe void DispatchIndirect(StructuredBuffer arguments, int byteOffset)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            if (arguments.IsDisposed)
                throw new ObjectDisposedException(nameof(arguments));
            if (arguments.ShaderAccess != BufferShaderAccess.ReadWrite)
                throw new ArgumentException("Indirect dispatch arguments live in a BufferShaderAccess.ReadWrite buffer.", nameof(arguments));
            if (byteOffset < 0 || (byteOffset % 4) != 0 || byteOffset + 12 > arguments.SizeInBytes)
                throw new ArgumentOutOfRangeException(nameof(byteOffset), "Three uints at a multiple of 4 bytes inside the buffer.");
            ApplyComputeState();
            MGG.GraphicsDevice_DispatchIndirect(Handle, arguments.Handle, byteOffset);
        }

        private unsafe void ApplyComputeState()
        {
            if (_computeShader == null)
                throw new InvalidOperationException("Apply a compute pass (EffectPass.IsCompute) before a dispatch.");

            BeginFrame();
            if (_computeShaderDirty)
            {
                MGG.GraphicsDevice_SetShader(Handle, ShaderStage.Compute, _computeShader.Handle);
                _computeShaderDirty = false;
            }
            _computeConstantBuffers.SetConstantBuffers(this);
            ComputeTextures.SetTextures(this);
            ComputeSamplerStates.PlatformSetSamplers(this);
            PushStorage(ShaderStage.Compute);
        }

        /// <summary>A new frame's command buffer: the compute stage's bindings are pushed again.</summary>
        private void DirtyComputeState()
        {
            _computeShaderDirty = _computeShader != null;
            ComputeTextures?.Dirty();
            ComputeSamplerStates?.Dirty();
            for (var i = 0; i < _storageDirty.Length; i++)
                _storageDirty[i] = uint.MaxValue;
        }

        internal void SetStorageBuffer(ShaderStage stage, int slot, StructuredBuffer buffer, bool writable)
        {
            var table = writable ? _writeStorage : _readStorage;
            ref var binding = ref table[(int)stage][slot];
            if (ReferenceEquals(binding.Resource, buffer) && binding.Writable == writable)
                return;
            binding.Resource = buffer;
            binding.Mip = 0;
            binding.Writable = writable;
            _storageDirty[(int)stage] |= writable ? 1u << (16 + slot) : 1u << slot;
        }

        internal void SetStorageImage(ShaderStage stage, int slot, Texture2D texture, int mip)
        {
            ref var binding = ref _writeStorage[(int)stage][slot];
            if (ReferenceEquals(binding.Resource, texture) && binding.Mip == mip)
                return;
            binding.Resource = texture;
            binding.Mip = mip;
            binding.Writable = true;
            _storageDirty[(int)stage] |= 1u << (16 + slot);
        }

        // Sends the changed storage bindings of a stage to the runtime.
        private unsafe void PushStorage(ShaderStage stage)
        {
            var dirty = _storageDirty[(int)stage];
            if (dirty == 0)
                return;

            var reads = _readStorage[(int)stage];
            for (var i = 0; i < StorageReadSlots; i++)
            {
                if ((dirty & (1u << i)) == 0 || reads[i].Resource is not StructuredBuffer buffer || buffer.IsDisposed)
                    continue;
                MGG.GraphicsDevice_SetShaderBuffer(Handle, stage, i, buffer.Handle, 0);
            }

            var writes = _writeStorage[(int)stage];
            for (var i = 0; i < StorageWriteSlots; i++)
            {
                if ((dirty & (1u << (16 + i))) == 0)
                    continue;
                if (writes[i].Resource is StructuredBuffer writable && !writable.IsDisposed)
                    MGG.GraphicsDevice_SetShaderBuffer(Handle, stage, i, writable.Handle, 1);
                else if (writes[i].Resource is Texture2D texture && !texture.IsDisposed)
                    MGG.GraphicsDevice_SetShaderImage(Handle, stage, i, texture.Handle, writes[i].Mip);
            }

            _storageDirty[(int)stage] = 0;
        }
    }
}
