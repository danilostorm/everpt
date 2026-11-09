// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.IO;

namespace Microsoft.Xna.Framework.Graphics
{

    // TODO: We should convert the types below 
    // into the start of a Shader reflection API.

    internal enum SamplerType
    {
        Sampler2D = 0,
        SamplerCube = 1,
        SamplerVolume = 2,
        Sampler1D = 3,
    }

    internal struct SamplerInfo
    {
        public SamplerType type;
        public int textureSlot;
        public int samplerSlot;
        public string name;
		public SamplerState state;

        // TODO: This should be moved to EffectPass.
        public int parameter;
    }

    internal struct VertexAttribute
    {
        public VertexElementUsage usage;
        public int index;

        // These should be obsolete once we move away from
        // the old C# graphics backends to the native ones.
        public string name;
        public int location;

        public string ToShaderSemantic()
        {
            switch (usage)
            {
                case VertexElementUsage.Position:
                    return "POSITION" + index;
                case VertexElementUsage.Color:
                    return "COLOR" + index;
                case VertexElementUsage.Normal:
                    return "NORMAL" + index;
                case VertexElementUsage.TextureCoordinate:
                    return "TEXCOORD" + index;
                case VertexElementUsage.BlendIndices:
                    return "BLENDINDICES" + index;
                case VertexElementUsage.BlendWeight:
                    return "BLENDWEIGHT" + index;
                case VertexElementUsage.Binormal:
                    return "BINORMAL" + index;
                case VertexElementUsage.Tangent:
                    return "TANGENT" + index;
                case VertexElementUsage.PointSize:
                    return "PSIZE" + index;
                case VertexElementUsage.Depth:
                    return "DEPTH" + index;
                case VertexElementUsage.Fog:
                    return "FOG" + index;
                case VertexElementUsage.Sample: // Huh?  What is this?
                    return "SAMPLE" + index;
                case VertexElementUsage.TessellateFactor:
                    return "TESSELLATEFACTOR" + index;
                default:
                    throw new NotSupportedException("Unknown vertex element usage!");
            }
        }
    }

    // Etervale fork (monogame/issues/005, 006, 007): the storage resources of a shader, as the effect compiler wrote
    // them (MGFX 12). The kinds match the effect compiler's MgfxStorageKind.
    internal enum StorageKind : byte
    {
        StructuredBuffer = 0,
        RWStructuredBuffer = 1,
        ByteAddressBuffer = 2,
        RWByteAddressBuffer = 3,
        RWTexture2D = 4,
        AccelerationStructure = 5,
    }

    internal struct StorageInfo
    {
        public StorageKind kind;
        /// <summary>The register: t for read-only buffers, u for RW resources.</summary>
        public int slot;
        /// <summary>The struct stride of a structured buffer, 4 for a raw buffer, 0 for an image.</summary>
        public int stride;
        /// <summary>The format a RWTexture2D declares (<c>[[vk::image_format]]</c>), or null.</summary>
        public SurfaceFormat? format;
        public string name;
        /// <summary>The effect parameter that holds the resource.</summary>
        public int parameter;

        public bool IsWritable => kind == StorageKind.RWStructuredBuffer || kind == StorageKind.RWByteAddressBuffer || kind == StorageKind.RWTexture2D;
    }

    /// <summary>Etervale fork: what a shader needs from the device beyond SM 6.0 (MGFX 12).</summary>
    [Flags]
    internal enum ShaderRequirements : uint
    {
        None = 0,
        /// <summary>Ray queries (SM 6.5 profiles, SPIR-V 1.5 with SPV_KHR_ray_query); the runtime does not support them yet.</summary>
        RayQuery = 1,
    }

    internal partial class Shader : GraphicsResource
	{
        /// <summary>Etervale fork: the storage buffers and images the shader reads or writes (MGFX 12).</summary>
        public StorageInfo[] Storage { get; private set; } = Array.Empty<StorageInfo>();

        /// <summary>Etervale fork: a compute shader's thread group size (<c>[numthreads(x, y, z)]</c>), else zeros.</summary>
        public int ThreadGroupSizeX { get; private set; }

        /// <summary>Etervale fork: see <see cref="ThreadGroupSizeX"/>.</summary>
        public int ThreadGroupSizeY { get; private set; }

        /// <summary>Etervale fork: see <see cref="ThreadGroupSizeX"/>.</summary>
        public int ThreadGroupSizeZ { get; private set; }

        /// <summary>
        /// Returns the platform specific shader profile identifier.
        /// </summary>
        public static int Profile { get { return PlatformProfile(); } }

        /// <summary>
        /// A hash value which can be used to compare shaders.
        /// </summary>
        internal int HashKey { get; private set; }

        public SamplerInfo[] Samplers { get; private set; }

	    public int[] CBuffers { get; private set; }

        public ShaderStage Stage { get; private set; }

        public VertexAttribute[] Attributes { get; private set; }

        public string Entrypoint { get; private set; }

        public string SourceFile { get; private set; }

        internal Shader(GraphicsDevice device, int version, BinaryReader reader)
        {
            GraphicsDevice = device;

            // Etervale fork: MGFX 12 writes the stage (compute shaders); MGFX 10-11 a vertex-shader flag.
            if (version >= 12)
                Stage = (ShaderStage)reader.ReadByte();
            else
                Stage = reader.ReadBoolean() ? ShaderStage.Vertex : ShaderStage.Pixel;

            if (version > 10)
            {
                // We don't really need these at runtime unless there is an
                // error with the shader and this information is very useful.
                SourceFile = reader.ReadString();
                Entrypoint = reader.ReadString();
            }
            else
            {
                SourceFile = "<unknown>";
                Entrypoint = "<unknown>";
            }

            var shaderLength = reader.ReadInt32();
            var shaderBytecode = reader.ReadBytes(shaderLength);

            var samplerCount = (int)reader.ReadByte();
            Samplers = new SamplerInfo[samplerCount];
            for (var s = 0; s < samplerCount; s++)
            {
                Samplers[s].type = (SamplerType)reader.ReadByte();
                Samplers[s].textureSlot = reader.ReadByte();
                Samplers[s].samplerSlot = reader.ReadByte();

				if (reader.ReadBoolean())
				{
					Samplers[s].state = new SamplerState();
					Samplers[s].state.AddressU = (TextureAddressMode)reader.ReadByte();
					Samplers[s].state.AddressV = (TextureAddressMode)reader.ReadByte();
					Samplers[s].state.AddressW = (TextureAddressMode)reader.ReadByte();
                    Samplers[s].state.BorderColor = new Color(
                        reader.ReadByte(), 
                        reader.ReadByte(), 
                        reader.ReadByte(), 
                        reader.ReadByte());
					Samplers[s].state.Filter = (TextureFilter)reader.ReadByte();
					Samplers[s].state.MaxAnisotropy = reader.ReadInt32();
					Samplers[s].state.MaxMipLevel = reader.ReadInt32();
					Samplers[s].state.MipMapLevelOfDetailBias = reader.ReadSingle();
				}

                Samplers[s].name = reader.ReadString();
                Samplers[s].parameter = reader.ReadByte();
            }

            var cbufferCount = (int)reader.ReadByte();
            CBuffers = new int[cbufferCount];
            for (var c = 0; c < cbufferCount; c++)
                CBuffers[c] = reader.ReadByte();

            var attributeCount = (int)reader.ReadByte();
            Attributes = new VertexAttribute[attributeCount];
            for (var a = 0; a < attributeCount; a++)
            {
                Attributes[a].name = reader.ReadString();
                Attributes[a].usage = (VertexElementUsage)reader.ReadByte();
                Attributes[a].index = reader.ReadByte();
                Attributes[a].location = reader.ReadInt16();
            }

            if (version >= 12)
                ReadEtervaleRecord(reader);

            PlatformConstruct(Stage, shaderBytecode);
        }

        // Etervale fork: the MGFX 12 part of a shader record (the effect compiler's ShaderData.Write): storage resources,
        // thread group size, requirements.
        private void ReadEtervaleRecord(BinaryReader reader)
        {
            var storageCount = (int)reader.ReadByte();
            Storage = new StorageInfo[storageCount];
            for (var s = 0; s < storageCount; s++)
            {
                Storage[s].kind = (StorageKind)reader.ReadByte();
                Storage[s].slot = reader.ReadByte();
                Storage[s].stride = reader.ReadInt32();
                var format = reader.ReadInt32();
                Storage[s].format = format < 0 ? (SurfaceFormat?)null : (SurfaceFormat)format;
                Storage[s].name = reader.ReadString();
                Storage[s].parameter = reader.ReadInt32();
            }

            ThreadGroupSizeX = reader.ReadInt32();
            ThreadGroupSizeY = reader.ReadInt32();
            ThreadGroupSizeZ = reader.ReadInt32();

            var requirements = (ShaderRequirements)reader.ReadUInt32();
            if ((requirements & ShaderRequirements.RayQuery) != 0)
            {
                throw new NotSupportedException(
                    $"Shader {Entrypoint} ({SourceFile}) uses ray queries (an SM 6.5 profile): the runtime does not enable " +
                    "VK_KHR_ray_query and acceleration structures.");
            }
        }

        internal protected override void GraphicsDeviceResetting()
        {
            PlatformGraphicsDeviceResetting();
        }
	}
}

