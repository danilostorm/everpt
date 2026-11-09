using Microsoft.Xna.Framework.Graphics;

namespace MonoGame.Effect
{
	// Etervale fork: the stage of a shader in MGFX 12 (the values of the framework's ShaderStage), which also has compute
	// shaders; MGFX 11 wrote a vertex-shader flag (monogame/issues/007).
	internal enum MgfxShaderStage : byte
	{
		Vertex = 0,
		Pixel = 1,
		Compute = 2,
	}

	// Etervale fork: the kinds of storage resources in MGFX 12 (the framework's StorageKind).
	internal enum MgfxStorageKind : byte
	{
		StructuredBuffer = 0,
		RWStructuredBuffer = 1,
		ByteAddressBuffer = 2,
		RWByteAddressBuffer = 3,
		RWTexture2D = 4,
		AccelerationStructure = 5,
	}

	// Etervale fork: what a shader needs from the device beyond SM 6.0 (the framework's ShaderRequirements).
	[System.Flags]
	internal enum MgfxShaderRequirements : uint
	{
		None = 0,
		RayQuery = 1,
	}

	internal partial class ShaderData
	{
		public ShaderData(MgfxShaderStage stage, int sharedIndex, byte[] bytecode)
		{
			Stage = stage;
			SharedIndex = sharedIndex;
			Bytecode = (byte[])bytecode.Clone();
		}

		// Etervale fork: the stage, in place of upstream's IsVertexShader.
		public MgfxShaderStage Stage { get; private set; }

		public bool IsVertexShader => Stage == MgfxShaderStage.Vertex;

        public string? SourceFile { get; set; }

        public string? Entrypoint { get; set; }

		public struct Sampler
		{
			public MojoShader.MOJOSHADER_samplerType type;
			public int textureSlot;
            public int samplerSlot;
			public string samplerName;
			public string parameterName;
			public int parameter;
			public SamplerState? state;
		}

		public struct Attribute
		{
            public string name;
            public VertexElementUsage usage;
			public int index;
#pragma warning disable 649
            public int location;
#pragma warning restore 649
        }

		// Etervale fork: a storage buffer or image of the shader (monogame/issues/005, 006).
		public struct Storage
		{
			public MgfxStorageKind kind;
			/// <summary>The register: t for read-only resources, u for RW ones.</summary>
			public int slot;
			/// <summary>Bytes per element of a structured buffer, 4 for a raw buffer, 0 for an image.</summary>
			public int stride;
			/// <summary>The SurfaceFormat a RWTexture2D declares, -1 for none.</summary>
			public int format;
			public string name;
			public int parameter;
		}

		/// <summary>
		/// The index to the constant buffers which are
		/// required by this shader at runtime.
		/// </summary>
		public int[] _cbuffers = [];

		public Sampler[] _samplers = [];

		public Attribute[] _attributes = [];

		// Etervale fork: MGFX 12.
		public Storage[] _storage = [];
		public int ThreadGroupX, ThreadGroupY, ThreadGroupZ;
		public MgfxShaderRequirements Requirements;

		public byte[] ShaderCode { get; set; } = [];


#region Non-Serialized Stuff

		public byte[] Bytecode { get; private set; }

		// The index of the shader in the shared list.
		public int SharedIndex { get; private set; }

        public string? ShaderProfile { get; set; }

#endregion // Non-Serialized Stuff

	}
}
