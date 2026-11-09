// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the public entry point of the effect compiler, for the content processor (SpirvEffectProcessor) and the
// effect tests (monogame/issues/007-mgcb-profiles.md).

using System.IO;

namespace MonoGame.Effect
{
    /// <summary>
    /// Compiles an effect (.fx) for the Vulkan profile into the MGFX 12 bytes the fork's framework loads: the steps of
    /// MonoGame's EffectProcessor (preprocess, parse techniques, compile every shader with DXC, write), without the
    /// content pipeline's types, so a test can run them too.
    /// </summary>
    public static class EffectCompiler
    {
        /// <summary>The MGFX format version written (the framework reads 10 to this).</summary>
        public const int MgfxVersion = EffectObject.Version;

        /// <summary>
        /// The options of a Vulkan build of <paramref name="sourceFile"/>. DXC's temporary files go next to
        /// <paramref name="outputFile"/> and are deleted afterwards.
        /// </summary>
        public static Options VulkanOptions(string sourceFile, string outputFile, bool debug, string? defines) => new()
        {
            SourceFile = sourceFile,
            OutputFile = outputFile,
            Profile = ShaderProfile.Vulkan,
            Debug = debug,
            Defines = defines,
        };

        /// <summary>
        /// Preprocesses the effect (includes, macros) and parses its techniques and passes; the result also lists the
        /// included files (<see cref="ShaderResult.Dependencies"/>).
        /// </summary>
        public static ShaderResult Parse(Options options, IEffectCompilerOutput output) =>
            ShaderResult.FromFile(options.SourceFile, options, output);

        /// <summary>
        /// Compiles every shader of the parsed effect and writes the MGFX bytes. On a compile error a
        /// <see cref="ShaderCompilerException"/> is thrown with DXC's messages already in <paramref name="errorsAndWarnings"/>.
        /// </summary>
        public static byte[] Compile(ShaderResult shaderResult, Options options, out string errorsAndWarnings)
        {
            var effect = EffectObject.CompileEffect(shaderResult, out errorsAndWarnings);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            effect.Write(writer, options);
            writer.Flush();
            // The written bytes only: upstream handed MemoryStream.GetBuffer() on, with the stream's unused capacity as
            // trailing zeros (the effects grew from 2.8 to 5.5 MB with MonoGame 3.8.5, research 12 §2).
            return stream.ToArray();
        }
    }
}
