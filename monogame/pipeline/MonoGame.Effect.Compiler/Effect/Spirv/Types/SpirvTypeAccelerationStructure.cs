// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

namespace MonoGame.Effect.Compiler.Effect.Spirv;

// Etervale fork: https://registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html#OpTypeAccelerationStructureKHR, the
// type of a RaytracingAccelerationStructure in the SM 6.5 profiles (monogame/issues/008-shader-model-6.5.md).
internal class SpirvTypeAccelerationStructure : SpirvTypeBase
{
    public override SpirvType Type => SpirvType.AccelerationStructure;

    public static SpirvTypeAccelerationStructure? Parse(string[] parts, SpirvReflectionInfo.SpirvParseContext context)
    {
        if (parts.Length < 1)
            return null;

        var id = parts[0];
        context.Names.TryGetValue(id, out var name);

        return new SpirvTypeAccelerationStructure
        {
            Id = id,
            Name = name
        };
    }
}
