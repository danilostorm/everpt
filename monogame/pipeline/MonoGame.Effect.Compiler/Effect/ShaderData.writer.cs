
using System.IO;

namespace MonoGame.Effect
{
    internal partial class ShaderData
    {
        public void Write(BinaryWriter writer, Options options)
        {
            // Etervale fork: MGFX 12 writes the stage (0 vertex, 1 pixel, 2 compute) where MGFX 11 wrote a vertex flag.
            writer.Write((byte)Stage);

            writer.Write(SourceFile ?? "<unknown>");
            writer.Write(Entrypoint ?? "<unknown>");

            writer.Write(ShaderCode.Length);
            writer.Write(ShaderCode);

            writer.Write((byte)_samplers.Length);
            foreach (var sampler in _samplers)
            {
                writer.Write((byte)sampler.type);
                writer.Write((byte)sampler.textureSlot);
                writer.Write((byte)sampler.samplerSlot);

				if (sampler.state != null)
				{
					writer.Write(true);
					writer.Write((byte)sampler.state.AddressU);
					writer.Write((byte)sampler.state.AddressV);
					writer.Write((byte)sampler.state.AddressW);
                    writer.Write(sampler.state.BorderColor.R);
                    writer.Write(sampler.state.BorderColor.G);
                    writer.Write(sampler.state.BorderColor.B);
                    writer.Write(sampler.state.BorderColor.A);
					writer.Write((byte)sampler.state.Filter);
					writer.Write(sampler.state.MaxAnisotropy);
					writer.Write(sampler.state.MaxMipLevel);
					writer.Write(sampler.state.MipMapLevelOfDetailBias);
				}
				else
					writer.Write(false);

                writer.Write(sampler.samplerName);

                writer.Write((byte)sampler.parameter);
            }

            writer.Write((byte)_cbuffers.Length);
            foreach (var cb in _cbuffers)
                writer.Write((byte)cb);

            writer.Write((byte)_attributes.Length);
            foreach (var attrib in _attributes)
            {
                writer.Write(attrib.name);
                writer.Write((byte)attrib.usage);
                writer.Write((byte)attrib.index);
                writer.Write((short)attrib.location);
            }

            // Etervale fork (MGFX 12, monogame/issues/007): the storage resources, the thread group size of a compute
            // shader, and the requirements beyond SM 6.0. Read by the framework's Shader.ReadEtervaleRecord.
            writer.Write((byte)_storage.Length);
            foreach (var storage in _storage)
            {
                writer.Write((byte)storage.kind);
                writer.Write((byte)storage.slot);
                writer.Write(storage.stride);
                writer.Write(storage.format);
                writer.Write(storage.name);
                writer.Write(storage.parameter);
            }

            writer.Write(ThreadGroupX);
            writer.Write(ThreadGroupY);
            writer.Write(ThreadGroupZ);
            writer.Write((uint)Requirements);
        }
    }
}
