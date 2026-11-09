using System;
using System.Diagnostics;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>
    /// Contains the rendering state for drawing with an effect. <para/>
    /// An effect can contain one or more passes.
    /// </summary>
    public class EffectPass
    {
        private readonly Effect _effect;

		private readonly Shader _pixelShader;
        private readonly Shader _vertexShader;
        private readonly Shader _computeShader;   // Etervale fork (monogame/issues/005)

        private readonly BlendState _blendState;
        private readonly DepthStencilState _depthStencilState;
        private readonly RasterizerState _rasterizerState;

        /// <summary>
        /// Gets the name of this pass.
        /// </summary>
		public string Name { get; private set; }

        /// <summary>
        /// Gets the collection of <see cref="EffectAnnotation"/> objects for this <see cref="EffectPass"/>.
        /// </summary>
        public EffectAnnotationCollection Annotations { get; private set; }

        internal EffectPass(    Effect effect, 
                                string name,
                                Shader vertexShader, 
                                Shader pixelShader, 
                                Shader computeShader,
                                BlendState blendState, 
                                DepthStencilState depthStencilState, 
                                RasterizerState rasterizerState,
                                EffectAnnotationCollection annotations )
        {
            Debug.Assert(effect != null, "Got a null effect!");
            Debug.Assert(annotations != null, "Got a null annotation collection!");

            _effect = effect;

            Name = name;

            _vertexShader = vertexShader;
            _pixelShader = pixelShader;
            _computeShader = computeShader;

            _blendState = blendState;
            _depthStencilState = depthStencilState;
            _rasterizerState = rasterizerState;

            Annotations = annotations;
        }
        
        internal EffectPass(Effect effect, EffectPass cloneSource)
        {
            Debug.Assert(effect != null, "Got a null effect!");
            Debug.Assert(cloneSource != null, "Got a null cloneSource!");

            _effect = effect;

            // Share all the immutable types.
            Name = cloneSource.Name;
            _blendState = cloneSource._blendState;
            _depthStencilState = cloneSource._depthStencilState;
            _rasterizerState = cloneSource._rasterizerState;
            Annotations = cloneSource.Annotations;
            _vertexShader = cloneSource._vertexShader;
            _pixelShader = cloneSource._pixelShader;
            _computeShader = cloneSource._computeShader;
        }

        /// <summary>
        /// Etervale fork: true for a compute pass (<c>ComputeShader = compile cs_6_0 F();</c>), which
        /// <see cref="Apply"/> prepares for <see cref="GraphicsDevice.Dispatch"/> instead of a draw.
        /// </summary>
        public bool IsCompute => _computeShader != null;

        /// <summary>
        /// Etervale fork: the thread group size of a compute pass (<c>[numthreads(x, y, z)]</c>): a dispatch of
        /// ⌈width / x⌉ × ⌈height / y⌉ groups covers a width × height image. Zeros for a graphics pass.
        /// </summary>
        public int ThreadGroupSizeX => _computeShader?.ThreadGroupSizeX ?? 0;

        /// <summary>Etervale fork: see <see cref="ThreadGroupSizeX"/>.</summary>
        public int ThreadGroupSizeY => _computeShader?.ThreadGroupSizeY ?? 0;

        /// <summary>Etervale fork: see <see cref="ThreadGroupSizeX"/>.</summary>
        public int ThreadGroupSizeZ => _computeShader?.ThreadGroupSizeZ ?? 0;

        /// <summary>
        /// Begins this pass.
        /// </summary>
        public void Apply()
        {
            // Set/get the correct shader handle/cleanups.

            var current = _effect.CurrentTechnique;
            _effect.OnApply();
            if (_effect.CurrentTechnique != current)
            {
                _effect.CurrentTechnique.Passes[0].Apply();
                return;
            }

            var device = _effect.GraphicsDevice;

            // Etervale fork: a compute pass sets the compute stage only; the dispatch follows (issue 005).
            if (_computeShader != null)
            {
                device.ComputeShader = _computeShader;
                SetShaderSamplers(_computeShader, device.ComputeTextures, device.ComputeSamplerStates);
                for (var c = 0; c < _computeShader.CBuffers.Length; c++)
                {
                    var cb = _effect.ConstantBuffers[_computeShader.CBuffers[c]];
                    cb.Update(_effect.Parameters);
                    device.SetConstantBuffer(ShaderStage.Compute, c, cb);
                }
                SetShaderStorage(_computeShader, device);
                return;
            }

            if (_vertexShader != null)
            {
                device.VertexShader = _vertexShader;

				// Update the texture parameters.
                SetShaderSamplers(_vertexShader, device.VertexTextures, device.VertexSamplerStates);
                SetShaderStorage(_vertexShader, device);

                // Update the constant buffers.
                for (var c = 0; c < _vertexShader.CBuffers.Length; c++)
                {
                    var cb = _effect.ConstantBuffers[_vertexShader.CBuffers[c]];
                    cb.Update(_effect.Parameters);
                    device.SetConstantBuffer(ShaderStage.Vertex, c, cb);
                }
            }

            if (_pixelShader != null)
            {
                device.PixelShader = _pixelShader;

                // Update the texture parameters.
                SetShaderSamplers(_pixelShader, device.Textures, device.SamplerStates);
                SetShaderStorage(_pixelShader, device);
                
                // Update the constant buffers.
                for (var c = 0; c < _pixelShader.CBuffers.Length; c++)
                {
                    var cb = _effect.ConstantBuffers[_pixelShader.CBuffers[c]];
                    cb.Update(_effect.Parameters);
                    device.SetConstantBuffer(ShaderStage.Pixel, c, cb);
                }
            }

            // Set the render states if we have some.
            if (_rasterizerState != null)
                device.RasterizerState = _rasterizerState;
            if (_blendState != null)
                device.BlendState = _blendState;
            if (_depthStencilState != null)
                device.DepthStencilState = _depthStencilState;
        }

        // Etervale fork (issues 005, 006): the storage buffers and images of a shader from their parameters, checked
        // against what the shader declares, so a mistake is an exception here and not a device error in a frame.
        private void SetShaderStorage(Shader shader, GraphicsDevice device)
        {
            foreach (var storage in shader.Storage)
            {
                var param = _effect.Parameters[storage.parameter];
                if (storage.kind == StorageKind.RWTexture2D)
                {
                    var texture = param.Data as Texture2D;
                    if (texture == null)
                        throw new InvalidOperationException($"Effect parameter '{storage.name}' (RWTexture2D, pass '{Name}') is not set.");
                    if (texture.IsDisposed)
                        throw new ObjectDisposedException(storage.name);
                    if (texture.ShaderAccess != TextureShaderAccess.ReadWrite)
                        throw new InvalidOperationException($"Effect parameter '{storage.name}' is written by the shader: its texture needs TextureShaderAccess.ReadWrite.");
                    if (storage.format.HasValue && texture.Format != storage.format.Value)
                        throw new InvalidOperationException($"Effect parameter '{storage.name}' declares {storage.format.Value} storage, but its texture is {texture.Format}.");
                    device.SetStorageImage(shader.Stage, storage.slot, texture, param.StorageMipLevel);
                    continue;
                }

                var buffer = param.Data as StructuredBuffer;
                if (buffer == null)
                    throw new InvalidOperationException($"Effect parameter '{storage.name}' ({storage.kind}, pass '{Name}') is not set.");
                if (buffer.IsDisposed)
                    throw new ObjectDisposedException(storage.name);
                if (storage.IsWritable && buffer.ShaderAccess != BufferShaderAccess.ReadWrite)
                    throw new InvalidOperationException($"Effect parameter '{storage.name}' is written by the shader: its buffer needs BufferShaderAccess.ReadWrite.");
                if ((storage.kind == StorageKind.StructuredBuffer || storage.kind == StorageKind.RWStructuredBuffer) && buffer.ElementStride != storage.stride)
                    throw new InvalidOperationException($"Effect parameter '{storage.name}' holds {storage.stride}-byte elements, but its buffer's stride is {buffer.ElementStride}.");
                device.SetStorageBuffer(shader.Stage, storage.slot, buffer, storage.IsWritable);
            }
        }

        private void SetShaderSamplers(Shader shader, TextureCollection textures, SamplerStateCollection samplerStates)
        {
            foreach (var sampler in shader.Samplers)
            {
                var param = _effect.Parameters[sampler.parameter];
                var texture = param.Data as Texture;

                textures[sampler.textureSlot] = texture;

                // If there is a sampler state set it.
                if (sampler.state != null)
                    samplerStates[sampler.samplerSlot] = sampler.state;
            }
        }
    }
}