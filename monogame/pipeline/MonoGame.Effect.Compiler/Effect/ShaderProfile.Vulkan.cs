// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Effect.Compiler.Effect.Spirv;

namespace MonoGame.Effect
{
    // Etervale fork (monogame/issues/005-008): besides vs_6_0/ps_6_0, compute shaders (cs_6_0), the SM 6.5 profiles
    // (vs_6_5, ps_6_5, cs_6_5: SPIR-V for Vulkan 1.2, so DXC can emit RayQuery with SPV_KHR_ray_query), and the storage
    // resources of SM 6: StructuredBuffer and ByteAddressBuffer (t registers), RWStructuredBuffer, RWByteAddressBuffer and
    // RWTexture2D (u registers), RaytracingAccelerationStructure (t, SM 6.5). A vs_6_0/ps_6_0 shader compiles exactly as
    // upstream's (same DXC arguments apart from the u-register shift, which only moves u registers they do not have).
    class VulkanShaderProfile : ShaderProfile
    {
        // The descriptor bindings of the registers (MGVK_TEXTURE_BINDING, MGVK_UAV_BINDING in the runtime).
        const int SlotOffset = 32;
        const int UavSlotOffset = 64;
        const int MaxUavSlots = 8;
        const int MaxTextureSlots = 16;

        public VulkanShaderProfile()
            : base("Vulkan", 80)
        {
        }

        internal override void AddMacros(Dictionary<string, string> macros)
        {
            macros.Add("SM6", "1");
            macros.Add("VULKAN", "1");
        }

        static readonly string[] VertexModels = ["vs_6_0", "vs_6_5"];
        static readonly string[] PixelModels = ["ps_6_0", "ps_6_5"];
        static readonly string[] ComputeModels = ["cs_6_0", "cs_6_5"];

        internal override void ValidateShaderModels(PassInfo pass)
        {
            if (!string.IsNullOrEmpty(pass.vsFunction))
            {
                if (!VertexModels.Contains(pass.vsModel))
                    throw new Exception($"Invalid Vulkan vertex profile '{pass.vsModel}'! Requires vs_6_0 (or vs_6_5).");
            }

            if (!string.IsNullOrEmpty(pass.psFunction))
            {
                if (!PixelModels.Contains(pass.psModel))
                    throw new Exception($"Invalid Vulkan pixel profile '{pass.psModel}'! Requires ps_6_0 (or ps_6_5).");
            }

            if (!string.IsNullOrEmpty(pass.csFunction))
            {
                if (!ComputeModels.Contains(pass.csModel))
                    throw new Exception($"Invalid Vulkan compute profile '{pass.csModel}'! Requires cs_6_0 (or cs_6_5).");
                if (!string.IsNullOrEmpty(pass.vsFunction) || !string.IsNullOrEmpty(pass.psFunction))
                    throw new Exception($"Pass '{pass.name}' mixes a compute shader with a vertex or pixel shader: a compute pass has the compute shader only.");
            }
        }

        enum VkDescriptorType : uint
        {
            SAMPLER = 0,
            COMBINED_IMAGE_SAMPLER = 1,
            SAMPLED_IMAGE = 2,
            STORAGE_IMAGE = 3,
            UNIFORM_TEXEL_BUFFER = 4,
            STORAGE_TEXEL_BUFFER = 5,
            UNIFORM_BUFFER = 6,
            STORAGE_BUFFER = 7,
            UNIFORM_BUFFER_DYNAMIC = 8,
            STORAGE_BUFFER_DYNAMIC = 9,
            INPUT_ATTACHMENT = 10,
            INLINE_UNIFORM_BLOCK = 1000138000,
            ACCELERATION_STRUCTURE_KHR = 1000150000,
            ACCELERATION_STRUCTURE_NV = 1000165000,
            SAMPLE_WEIGHT_IMAGE_QCOM = 1000440000,
            BLOCK_MATCH_IMAGE_QCOM = 1000440001,
            MUTABLE_EXT = 1000351000,
            INLINE_UNIFORM_BLOCK_EXT = INLINE_UNIFORM_BLOCK,
            MUTABLE_VALVE = MUTABLE_EXT,
            MAX_ENUM = 0x7FFFFFFF
        };

        [Flags]
        enum VkShaderStageFlags : uint
        {
            VERTEX_BIT = 0x00000001,
            TESSELLATION_CONTROL_BIT = 0x00000002,
            TESSELLATION_EVALUATION_BIT = 0x00000004,
            GEOMETRY_BIT = 0x00000008,
            FRAGMENT_BIT = 0x00000010,
            COMPUTE_BIT = 0x00000020,
            ALL_GRAPHICS = 0x0000001F,
            ALL = 0x7FFFFFFF,
            RAYGEN_BIT_KHR = 0x00000100,
            ANY_HIT_BIT_KHR = 0x00000200,
            CLOSEST_HIT_BIT_KHR = 0x00000400,
            MISS_BIT_KHR = 0x00000800,
            INTERSECTION_BIT_KHR = 0x00001000,
            CALLABLE_BIT_KHR = 0x00002000,
            TASK_BIT_EXT = 0x00000040,
            MESH_BIT_EXT = 0x00000080,
            SUBPASS_SHADING_BIT_HUAWEI = 0x00004000,
            CLUSTER_CULLING_BIT_HUAWEI = 0x00080000,
            RAYGEN_BIT_NV = RAYGEN_BIT_KHR,
            ANY_HIT_BIT_NV = ANY_HIT_BIT_KHR,
            CLOSEST_HIT_BIT_NV = CLOSEST_HIT_BIT_KHR,
            MISS_BIT_NV = MISS_BIT_KHR,
            INTERSECTION_BIT_NV = INTERSECTION_BIT_KHR,
            CALLABLE_BIT_NV = CALLABLE_BIT_KHR,
            TASK_BIT_NV = TASK_BIT_EXT,
            MESH_BIT_NV = MESH_BIT_EXT,
            FLAG_BITS_MAX_ENUM = 0x7FFFFFFF
        };

        struct VkDescriptorSetLayoutBinding
        {
            public uint binding;
            public VkDescriptorType descriptorType;
            public uint descriptorCount;
            public VkShaderStageFlags stageFlags;
            public nint pImmutableSamplers;
        };

        // Etervale fork: the DXC arguments of a shader, without the reflection switch and the files. Kept apart so a test
        // can check that a vs_6_0/ps_6_0 shader is compiled with upstream's arguments.
        internal static string DxcArguments(MgfxShaderStage stage, string shaderModel)
        {
            var toolArgs = "";
            toolArgs += "-nologo ";
            toolArgs += "-spirv ";
            toolArgs += "-fvk-use-dx-layout ";

            if (stage == MgfxShaderStage.Vertex)
            {
                toolArgs += "-fvk-invert-y ";
                toolArgs += "-fvk-use-dx-position-w ";
            }
            else if (stage == MgfxShaderStage.Pixel)
            {
                // Move pixel shaders into the second descriptor
                // to avoid overlapping bindings between the vertex
                // and pixel stages.
                toolArgs += "-auto-binding-space 1 ";
            }
            // A compute shader is alone in its pipeline: descriptor set 0.

            // Offset the texture/sampler slots?
            toolArgs += $"-fvk-t-shift {SlotOffset} all ";
            toolArgs += $"-fvk-s-shift {SlotOffset} all ";
            // Etervale fork: the u registers (RW resources) after the textures.
            toolArgs += $"-fvk-u-shift {UavSlotOffset} all ";

            // Etervale fork: SM 6.5 for ray queries, which need SPIR-V 1.4 or later: the Vulkan 1.2 environment (SPIR-V 1.5).
            if (shaderModel.EndsWith("_6_5", StringComparison.Ordinal))
                toolArgs += "-fspv-target-env=vulkan1.2 ";

            toolArgs += "-T " + shaderModel + " ";
            toolArgs += "-E main ";
            return toolArgs;
        }

        internal override ShaderData CreateShader(ShaderResult shaderResult, string shaderFunction, string shaderProfile, MgfxShaderStage stage, EffectObject effect, ref string errorsAndWarnings)
        {
            bool isVertexShader = stage == MgfxShaderStage.Vertex;

            var outputPath = Path.GetDirectoryName(shaderResult.OutputFilePath) ?? "";
            var sourceFileName = Path.GetFileNameWithoutExtension(shaderResult.FilePath) + "." + shaderFunction;

            // TODO: We have no intermediate folder in 2MGFX for temp stuff
            // that isn't content, but could be useful later.  So just putting
            // them into the output then cleaning it up after.
            var intermediateDir = outputPath;
            var hlslFile = Path.Combine(intermediateDir, sourceFileName + ".hlsl");
            var binFile = Path.Combine(intermediateDir, sourceFileName + ".bin");
            var reflectFile = Path.Combine(intermediateDir, sourceFileName + ".reflect");

            // Need to keep this for debugging to work.
            var dbgFile = Path.Combine(outputPath, sourceFileName + ".dbg");

            // Disable this if you want to keep these around for testing!
            var cleanup = new List<string>();
            cleanup.Add(hlslFile);
            cleanup.Add(binFile);
            cleanup.Add(dbgFile);
            cleanup.Add(reflectFile);

            try
            {
                if (!string.IsNullOrEmpty(intermediateDir) && !Directory.Exists(intermediateDir))
                    Directory.CreateDirectory(intermediateDir);

                // Replace the entrypoint name with "main" for simplicity at runtime.
                var shaderContent = Regex.Replace(shaderResult.FileContent, @"(?<=\s+)" + shaderFunction + @"(?=\s*[(])", "main");

                // Write preprocessed hlsl file.
                File.WriteAllText(hlslFile, shaderContent);

                string stdout = string.Empty;
                string stderr = string.Empty;
                string toolArgs = DxcArguments(stage, shaderProfile);
                int toolResult;

                // Adds HLSL specific reflection information to the SPIR-V
                // https://github.com/Microsoft/DirectXShaderCompiler/blob/main/docs/SPIR-V.rst#reflection
                var reflectArgs = toolArgs + "-fspv-reflect ";
                var files = "-Fc \"" + reflectFile + "\" " + "-Fo \"" + binFile + "\" ";
                var debug = shaderResult.Debug ? "-Zi " : "";
                // Error: '-Fd cannot be used with -spirv' - investigate

                // Ok first build gathers reflection info.
                toolResult = Dxc.Run(reflectArgs + files + debug + "\"" + hlslFile + "\"", out stdout, out stderr);
                errorsAndWarnings += stderr;

                if (toolResult != 0)
                {
                    errorsAndWarnings += $"DXC.exe returned error code '{toolResult}'.\n";
                    errorsAndWarnings += stdout;
                    throw new ShaderCompilerException();
                }

                string[] reflectionDataArray = File.ReadAllLines(reflectFile);
                SpirvReflectionInfo reflectionInfo = SpirvReflectionInfo.Parse(reflectionDataArray);

                // This second build is for generating the shader binary without
                // reflection info that forces Google VK extensions into the binary.
                {
                    toolResult = Dxc.Run(toolArgs + files + debug + "\"" + hlslFile + "\"", out stdout, out stderr);
                    errorsAndWarnings += stderr;

                    if (toolResult != 0)
                    {
                        errorsAndWarnings += $"DXC.exe returned error code '{toolResult}'.\n";
                        errorsAndWarnings += stdout;
                        throw new ShaderCompilerException();
                    }
                }

                // Load up the compiled shader.
                var bytecode = File.ReadAllBytes(binFile);

                // First look to see if we already created this same shader.
                foreach (var shader in effect.Shaders)
                {
                    if (bytecode.SequenceEqual(shader.Bytecode))
                        return shader;
                }

                // Keep the debug file if we are creating a new shader
                // and debug shaders are enabled.
                if (shaderResult.Debug)
                {
                    if (File.Exists(dbgFile))
                        shaderResult.AdditionalOutputFiles.Add(dbgFile);

                    cleanup.Remove(dbgFile);
                }

                // Create a new shader.
                var shaderData = new ShaderData(stage, effect.Shaders.Count, bytecode);
                var samplers = new List<ShaderData.Sampler>();
                var storage = new List<ShaderData.Storage>();

                int cbCount = 0;
                var cbufferIndex = new List<int>();

                // we could have multiple descriptor sets in here, but we don't currently handle that
                var withDescriptorSet = reflectionInfo.Variables.Where(v => v.DescriptorSet.HasValue);

                foreach (SpirvVariable variable in withDescriptorSet)
                {
                    // Etervale fork: storage buffers, storage images and acceleration structures (issues 005, 006, 008).
                    if (TryAddStorage(variable, storage, ref errorsAndWarnings))
                        continue;

                    if (variable.Pointer.PointerType.Type == SpirvType.Struct)
                    {
                        // TODO: Look into multiple cbuffer support.
                        if (cbCount > 0)
                        {
                            errorsAndWarnings += "Building effects for Vulkan currently doesn't support more than one constant buffer (cbuffer) structures. Please consider refactoring your HLSL code.";
                            throw new ShaderCompilerException();
                        }

                        if (variable.Pointer.PointerType is not SpirvTypeStruct constantBuffer)
                        {
                            errorsAndWarnings += $"Type for {variable.Pointer.Name ?? variable.Pointer.Id} was `Struct` but PointerType was not `SpirvTypeStruct`.";
                            throw new ShaderCompilerException();
                        }

                        var cbuffer = ConstantBufferData.BuildFromSpirvStruct(constantBuffer);
                        if (cbuffer.Size > 0)
                        {
                            var match = effect.ConstantBuffers.FindIndex(e => e.SameAs(cbuffer));

                            if (match == -1)
                            {
                                cbufferIndex.Add(effect.ConstantBuffers.Count);
                                effect.ConstantBuffers.Add(cbuffer);
                            }
                            else
                                cbufferIndex.Add(match);
                        }

                        cbCount++;
                    }
                    else if (variable.Pointer.PointerType.Type == SpirvType.Image)
                    {
                        // find all the times this image was sampled by distinct samplers.
                        var sampledImages = reflectionInfo.SampledImages.Where(si => si.LoadedImage.Variable == variable)
                            .DistinctBy(si => si.LoadedSampler.Variable);

                        // and create a sampler parameter for each.
                        foreach (var sampledImage in sampledImages)
                        {
                            // pull out what we need from the sampled image object
                            var samplerVariable = sampledImage.LoadedSampler.Variable;
                            var imageVariable = sampledImage.LoadedImage.Variable;

                            var samplerType = (SpirvTypeSampler)samplerVariable.Pointer.PointerType;
                            var imageType = (SpirvTypeImage)imageVariable.Pointer.PointerType;

                            var sampler = new ShaderData.Sampler
                            {
                                samplerSlot = (int)(samplerVariable.BindingSlot ?? 0) - SlotOffset,
                                samplerName = samplerVariable.Name ?? samplerVariable.Id,
                                textureSlot = (int)(imageVariable.BindingSlot ?? 0) - SlotOffset,
                            };

                            // This image is only sampled by one sampler, we can safely use the texture name for the parameter.
                            if (sampledImages.Count() == 1)
                            {
                                sampler.parameterName = imageVariable.Name ?? imageVariable.Id;
                            }
                            // otherwise make a composite name for this image/sampler combo.
                            else
                            {
                                sampler.parameterName = $"{samplerVariable.Name ?? imageVariable.Id}+{imageVariable.Name ?? imageVariable.Id}";
                            }

                            switch (imageType.Dimensionality)
                            {
                                case ImageDimensionality.OneD:
                                    sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_1D;
                                    break;
                                case ImageDimensionality.TwoD:
                                    sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_2D;
                                    break;
                                case ImageDimensionality.ThreeD:
                                    sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_VOLUME;
                                    break;
                                case ImageDimensionality.Cube:
                                    sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_CUBE;
                                    break;
                            }

                            if (!shaderResult.ShaderInfo.SamplerStates.TryGetValue(samplerVariable.Name ?? samplerVariable.Id, out SamplerStateInfo? samplerStateInfo))
                            {
                                errorsAndWarnings += $"Could not find sampler state info for sampler '{samplerVariable.Name}'; using defaults\n";
                                samplerStateInfo = new SamplerStateInfo();
                            }

                            sampler.state = samplerStateInfo.State;
                            samplers.Add(sampler);
                        }

                        if (!sampledImages.Any())
                        {
                            // This is for handling textures that are read via .Load and not a sampler. Fork (issue 011):
                            // registered even when a sampler_state names the texture. The upstream code skipped those, so
                            // a shader that only loads such a texture had no binding for it and read zeros.
                            var imageLoads = reflectionInfo.ImageLoads.Where(si => si.Variable == variable).DistinctBy(si => si.Variable);
                            foreach (var image in imageLoads)
                            {
                                var imageVariable = image.Variable;
                                var imageType = (SpirvTypeImage)imageVariable.Pointer.PointerType;

                                var sampler = new ShaderData.Sampler
                                {
                                    samplerSlot = -1,
                                    samplerName = string.Empty,
                                    textureSlot = (int)(imageVariable.BindingSlot ?? 0) - SlotOffset,
                                };

                                sampler.parameterName = imageVariable.Name ?? imageVariable.Id;

                                switch (imageType.Dimensionality)
                                {
                                    case ImageDimensionality.OneD:
                                        sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_1D;
                                        break;
                                    case ImageDimensionality.TwoD:
                                        sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_2D;
                                        break;
                                    case ImageDimensionality.ThreeD:
                                        sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_VOLUME;
                                        break;
                                    case ImageDimensionality.Cube:
                                        sampler.type = MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_CUBE;
                                        break;
                                }

                                sampler.state = null;
                                samplers.Add(sampler);
                            }
                        }
                    }
                }

                // Gather the input attributes.
                var attributes = new List<ShaderData.Attribute>();
                if (isVertexShader)
                {
                    // Sort by the location.
                    var sorted = reflectionInfo.Input.OrderBy(i => i.Location);

                    foreach (SpirvVariable input in sorted)
                    {
                        var semanticId = input.HlslSemantic ?? input.Id.Replace("%in_var_", "");

                        var m = Regex.Match(semanticId, @"(\D+)(\d+)?");
                        int indexOffset = m.Groups[2].Success
                            ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                            : 0;

                        var usage = VertexElementUsage.TextureCoordinate;

                        if (m.Groups[1].Success)
                        {
                            switch (m.Groups[1].Value.ToUpper(CultureInfo.InvariantCulture))
                            {
                                default:
                                    // Give a warning which hopefully someone notices.
                                    errorsAndWarnings += $"Unknown vertex shader input semantic `{m.Groups[1].Value}`; defaulting to texture coord.\n";
                                    usage = VertexElementUsage.TextureCoordinate;
                                    break;
                                case "TEXCOORD":
                                    usage = VertexElementUsage.TextureCoordinate;
                                    break;
                                // NOTE: Some shaders incorrectly pass in SV_POSITION to
                                // the vertex shader which is allowed in DX11, so we allow
                                // it here as well.
                                case "SV_POSITION":
                                case "POSITION":
                                    usage = VertexElementUsage.Position;
                                    break;
                                case "NORMAL":
                                    usage = VertexElementUsage.Normal;
                                    break;
                                case "TANGENT":
                                    usage = VertexElementUsage.Tangent;
                                    break;
                                case "BINORMAL":
                                    usage = VertexElementUsage.Binormal;
                                    break;
                                case "COLOR":
                                    usage = VertexElementUsage.Color;
                                    break;
                                case "BLENDINDICES":
                                    usage = VertexElementUsage.BlendIndices;
                                    break;
                                case "BLENDWEIGHT":
                                    usage = VertexElementUsage.BlendWeight;
                                    break;
                                case "DEPTH":
                                    usage = VertexElementUsage.Depth;
                                    break;
                                case "FOG":
                                    usage = VertexElementUsage.Fog;
                                    break;
                                case "POINTSIZE":
                                    usage = VertexElementUsage.PointSize;
                                    break;
                                case "TESSELLATEFACTOR":
                                    usage = VertexElementUsage.TessellateFactor;
                                    break;
                            }
                        }

                        uint locationCount = 1;
                        var pointerType = input.Pointer?.PointerType;

                        if (pointerType is SpirvTypeArray spirvTypeArray)
                        {
                            locationCount = spirvTypeArray.Length;
                            if (spirvTypeArray.ElementType is SpirvTypeMatrix spirvTypeMatrix)
                            {
                                locationCount *= spirvTypeMatrix.Columns;
                            }
                        }
                        else if (pointerType is SpirvTypeMatrix spirvTypeMatrix)
                        {
                            locationCount = spirvTypeMatrix.Columns;
                        }

                        for (int locationIndex = 0; locationIndex < locationCount; locationIndex++)
                        {
                            var a = new ShaderData.Attribute
                            {
                                usage = usage,
                                index = indexOffset + locationIndex,

                                // TODO: These are unused at runtime under the
                                // new native backends, we will remove them soon.
                                location = 0,
                                name = string.Empty,
                            };

                            attributes.Add(a);
                        }
                    }
                }

                // Etervale fork: the RW resources of SM 6 are for compute shaders (a pixel shader writing a storage
                // image or buffer needs device features the runtime does not enable).
                if (stage != MgfxShaderStage.Compute && storage.Any(s => IsWritable(s.kind)))
                {
                    errorsAndWarnings += $"{shaderFunction}: RW resources (RWTexture2D, RWStructuredBuffer, RWByteAddressBuffer) are allowed in compute shaders only.\n";
                    throw new ShaderCompilerException();
                }

                shaderData._samplers = samplers.ToArray();
                shaderData._cbuffers = cbufferIndex.ToArray();
                shaderData._attributes = attributes.ToArray();
                shaderData._storage = storage.ToArray();
                if (stage == MgfxShaderStage.Compute)
                {
                    shaderData.ThreadGroupX = (int)reflectionInfo.LocalSize[0];
                    shaderData.ThreadGroupY = (int)reflectionInfo.LocalSize[1];
                    shaderData.ThreadGroupZ = (int)reflectionInfo.LocalSize[2];
                }
                if (reflectionInfo.Capabilities.Contains("RayQueryKHR") || storage.Any(s => s.kind == MgfxStorageKind.AccelerationStructure))
                {
                    // DXC 1.9's SPIR-V back end emits a ray query for a _6_0 target too, in SPIR-V 1.0 (measured), while
                    // VK_KHR_ray_query depends on VK_KHR_spirv_1_4: the SM 6.5 profiles, SPIR-V 1.5 for Vulkan 1.2, are
                    // the ray-query ones.
                    if (!shaderProfile.EndsWith("_6_5", StringComparison.Ordinal))
                    {
                        errorsAndWarnings += $"{shaderFunction}: ray queries and acceleration structures need a shader model 6.5 profile (vs_6_5, ps_6_5, cs_6_5), not {shaderProfile}.\n";
                        throw new ShaderCompilerException();
                    }
                    shaderData.Requirements |= MgfxShaderRequirements.RayQuery;
                }

                // Generate the layout bindings from our cbuffers, samplers, and textures.
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    var bindings = new List<VkDescriptorSetLayoutBinding>();

                    VkDescriptorSetLayoutBinding binding;
                    binding.stageFlags = stage switch
                    {
                        MgfxShaderStage.Vertex => VkShaderStageFlags.VERTEX_BIT,
                        MgfxShaderStage.Pixel => VkShaderStageFlags.FRAGMENT_BIT,
                        _ => VkShaderStageFlags.COMPUTE_BIT,
                    };
                    binding.pImmutableSamplers = 0;
                    binding.descriptorCount = 1;

                    // Write the number of uniform buffers
                    writer.Write(cbufferIndex.Count);

                    uint uniformSlots = 0;
                    uint textureSlots = 0;
                    uint samplerSlots = 0;
                    var textureTypes = new uint[16];

                    // We just have one cbuffer at 0 right now.
                    if (cbufferIndex.Count > 0)
                    {
                        uniformSlots |= 1 << 0;
                        binding.binding = 0;
                        binding.descriptorType = VkDescriptorType.UNIFORM_BUFFER_DYNAMIC;
                        bindings.Add(binding);
                    }

                    foreach (var s in samplers)
                    {
                        if (s.textureSlot == s.samplerSlot)
                        {
                            textureTypes[s.textureSlot] = ToTextureType(s.type);

                            textureSlots |= (uint)(1 << s.textureSlot);
                            samplerSlots |= (uint)(1 << s.textureSlot);
                            binding.binding = (uint)(s.textureSlot + SlotOffset);
                            binding.descriptorType = VkDescriptorType.COMBINED_IMAGE_SAMPLER;
                            bindings.Add(binding);
                            continue;
                        }

                        // Fork (issue 011): slot 0 is a slot (a texture loaded at t0, a sampler at s0 sampling
                        // another register); "> 0" left them without a binding. No sampler is -1.
                        if (s.samplerSlot >= 0)
                        {
                            samplerSlots |= (uint)(1 << s.samplerSlot);
                            binding.binding = (uint)(s.samplerSlot + SlotOffset);
                            binding.descriptorType = VkDescriptorType.SAMPLER;
                            bindings.Add(binding);
                        }

                        if (s.textureSlot >= 0)
                        {
                            textureTypes[s.textureSlot] = ToTextureType(s.type);

                            textureSlots |= (uint)(1 << s.textureSlot);
                            binding.binding = (uint)(s.textureSlot + SlotOffset);
                            binding.descriptorType = VkDescriptorType.SAMPLED_IMAGE;
                            bindings.Add(binding);
                        }
                    }

                    // Etervale fork: the storage resources, at their register's binding (the runtime derives its slot
                    // masks from these bindings, monogame/runtime MGG_Shader_Create).
                    foreach (var s in storage)
                    {
                        binding.binding = (uint)(s.slot + (IsWritable(s.kind) ? UavSlotOffset : SlotOffset));
                        binding.descriptorType = s.kind switch
                        {
                            MgfxStorageKind.RWTexture2D => VkDescriptorType.STORAGE_IMAGE,
                            MgfxStorageKind.AccelerationStructure => VkDescriptorType.ACCELERATION_STRUCTURE_KHR,
                            _ => VkDescriptorType.STORAGE_BUFFER,
                        };
                        bindings.Add(binding);
                    }

                    // Write the slot bits.
                    writer.Write(uniformSlots);
                    writer.Write(textureSlots);
                    writer.Write(samplerSlots);

                    // Write the texture types.
                    for (int i = 0; i < textureTypes.Length; i++)
                        writer.Write(textureTypes[i]);

                    // Write the bindings.
                    writer.Write((uint)bindings.Count);
                    foreach (var b in bindings)
                    {
                        writer.Write(b.binding);
                        writer.Write((uint)b.descriptorType);
                        writer.Write(b.descriptorCount);
                        writer.Write((uint)b.stageFlags);
                        writer.Write((UInt64)b.pImmutableSamplers);
                    }

                    // Finally write the shader bytecode.
                    writer.Write(shaderData.Bytecode);

                    // Store the combined binding layout info and shader code.
                    shaderData.ShaderCode = stream.ToArray();
                }

                effect.Shaders.Add(shaderData);

                return shaderData;
            }
            finally
            {
                foreach (var file in cleanup)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }
        }

        static bool IsWritable(MgfxStorageKind kind) =>
            kind == MgfxStorageKind.RWStructuredBuffer || kind == MgfxStorageKind.RWByteAddressBuffer || kind == MgfxStorageKind.RWTexture2D;

        // Etervale fork: a storage resource from its HLSL type (UserTypeGOOGLE): false for anything else (cbuffers,
        // textures, samplers), which the upstream code handles. Unsupported RW types and UAV counters are errors here,
        // rather than resources the runtime would bind wrongly.
        static bool TryAddStorage(SpirvVariable variable, List<ShaderData.Storage> storage, ref string errorsAndWarnings)
        {
            var name = variable.Name ?? variable.Id;
            if (name.StartsWith("counter.var.", StringComparison.Ordinal))
            {
                errorsAndWarnings += $"'{name.Substring("counter.var.".Length)}': UAV counters (IncrementCounter, DecrementCounter, Append/ConsumeStructuredBuffer) are not supported.\n";
                throw new ShaderCompilerException();
            }

            var userType = variable.HlslUserType ?? "";
            var baseType = userType.Split(':')[0];
            MgfxStorageKind kind;
            switch (baseType)
            {
                case "structuredbuffer": kind = MgfxStorageKind.StructuredBuffer; break;
                case "rwstructuredbuffer": kind = MgfxStorageKind.RWStructuredBuffer; break;
                case "byteaddressbuffer": kind = MgfxStorageKind.ByteAddressBuffer; break;
                case "rwbyteaddressbuffer": kind = MgfxStorageKind.RWByteAddressBuffer; break;
                case "rwtexture2d": kind = MgfxStorageKind.RWTexture2D; break;
                case "raytracingaccelerationstructure": kind = MgfxStorageKind.AccelerationStructure; break;
                case "rwtexture1d":
                case "rwtexture1darray":
                case "rwtexture2darray":
                case "rwtexture3d":
                case "rwbuffer":
                case "buffer":
                case "appendstructuredbuffer":
                case "consumestructuredbuffer":
                case "rasterizerorderedtexture2d":
                    errorsAndWarnings += $"'{name}' ({userType}): not supported by the Etervale fork's Vulkan profile; use RWTexture2D, StructuredBuffer, RWStructuredBuffer, ByteAddressBuffer or RWByteAddressBuffer.\n";
                    throw new ShaderCompilerException();
                default:
                    if (variable.Pointer.PointerType is SpirvTypeAccelerationStructure)
                    {
                        kind = MgfxStorageKind.AccelerationStructure;
                        break;
                    }
                    return false;
            }

            var binding = (int)(variable.BindingSlot ?? 0);
            var offset = IsWritable(kind) ? UavSlotOffset : SlotOffset;
            var slot = binding - offset;
            var slots = IsWritable(kind) ? MaxUavSlots : MaxTextureSlots;
            if (slot < 0 || slot >= slots)
            {
                errorsAndWarnings += $"'{name}': register {(IsWritable(kind) ? "u" : "t")}{slot} is outside {(IsWritable(kind) ? "u0-u7" : "t0-t15")}; give it an explicit register in range.\n";
                throw new ShaderCompilerException();
            }

            var entry = new ShaderData.Storage { kind = kind, slot = slot, name = name, format = -1, parameter = -1 };
            switch (kind)
            {
                case MgfxStorageKind.StructuredBuffer:
                case MgfxStorageKind.RWStructuredBuffer:
                    entry.stride = (int)StructuredStride(variable);
                    if (entry.stride <= 0)
                    {
                        errorsAndWarnings += $"'{name}': the element stride of the structured buffer could not be read from the SPIR-V.\n";
                        throw new ShaderCompilerException();
                    }
                    break;
                case MgfxStorageKind.ByteAddressBuffer:
                case MgfxStorageKind.RWByteAddressBuffer:
                    entry.stride = 4;
                    break;
                case MgfxStorageKind.RWTexture2D:
                    var image = variable.Pointer.PointerType as SpirvTypeImage;
                    if (image == null || image.Dimensionality != ImageDimensionality.TwoD || image.Arrayed || image.Multisampled)
                    {
                        errorsAndWarnings += $"'{name}': a RWTexture2D must be a single-sample 2D image.\n";
                        throw new ShaderCompilerException();
                    }
                    if (!TryStorageFormat(image.ImageFormat, out entry.format))
                    {
                        errorsAndWarnings += $"'{name}': storage format '{image.ImageFormat}' is not supported; declare one of " +
                            "rgba32f, rgba16f, rg32f, rg16f, r32f, rgba8, rgba16, rg16 or r8 with [[vk::image_format(\"...\")]].\n";
                        throw new ShaderCompilerException();
                    }
                    break;
            }

            storage.Add(entry);
            return true;
        }

        // The struct stride of a structured buffer: the ArrayStride of the runtime array that is the block's only member.
        static uint StructuredStride(SpirvVariable variable)
        {
            if (variable.Pointer.PointerType is SpirvTypeStruct block && block.Members.Count == 1 &&
                block.Members[0].Type is SpirvTypeRuntimeArray array)
                return array.ArrayStride;
            return 0;
        }

        // The SurfaceFormat of a storage image format (the runtime's ToVkFormat gives these Vulkan formats back), or
        // false when the format is unknown (an undeclared format would need shaderStorageImageWriteWithoutFormat).
        static bool TryStorageFormat(string spirvFormat, out int surfaceFormat)
        {
            SurfaceFormat? format = spirvFormat switch
            {
                "Rgba32f" => SurfaceFormat.Vector4,
                "Rgba16f" => SurfaceFormat.HalfVector4,
                "Rg32f" => SurfaceFormat.Vector2,
                "Rg16f" => SurfaceFormat.HalfVector2,
                "R32f" => SurfaceFormat.Single,
                "Rgba8" => SurfaceFormat.Color,
                "Rgba16" => SurfaceFormat.Rgba64,
                "Rg16" => SurfaceFormat.Rg32,
                "R8" => SurfaceFormat.Alpha8,
                _ => null,
            };
            surfaceFormat = format.HasValue ? (int)format.Value : -1;
            return format.HasValue;
        }

        private static uint ToTextureType(MojoShader.MOJOSHADER_samplerType type)
        {
            // NOTE:  This matches MGTextureType in the native bindings.
            // Ideally we would move to this in the shader API too.
            switch (type)
            {
                default:
                case MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_2D:
                    return 0;
                case MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_VOLUME:
                    return 1;
                case MojoShader.MOJOSHADER_samplerType.MOJOSHADER_SAMPLER_CUBE:
                    return 2;
            }
        }
    }
}
