using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Op = MonoGame.Interop.WebStream.Op;

namespace MonoGame.Interop;

// The browser's implementation of the fork's graphics runtime (Graphics.Interop.cs): see README.md and mgweb.js.
[SupportedOSPlatform("browser")]
internal static unsafe partial class MGG
{
    // The MGFX profile the effects are built for (the Vulkan build's), since the web effects are the same files with
    // their shaders converted.
    private const int ShaderProfileVulkan = 80;
    private const int TextureFlagRenderTarget = 1;
    private const int TextureFlagStorage = 2;

    private static readonly nint DeviceName = Marshal.StringToCoTaskMemUTF8("WebGPU");
    private static readonly nint Description = Marshal.StringToCoTaskMemUTF8("The browser's WebGPU adapter");

    private static int Id(void* handle) => (int)(nint)handle;

    private static void SetDisplayMode(ref MGG_DisplayMode mode)
    {
        mode.format = SurfaceFormat.Color;
        mode.width = 1920;
        mode.height = 1080;
    }

    public static partial void EffectResource_GetBytecode(string name, out byte* bytecode, out int size)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "BuiltinEffects", name + ".mgfxo"));
        bytecode = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        size = bytes.Length;
        Marshal.Copy(bytes, 0, (nint)bytecode, bytes.Length);
    }

    public static partial MGG_GraphicsSystem* GraphicsSystem_Create()
    {
        WebStream.EnsureStarted();
        return (MGG_GraphicsSystem*)1;
    }

    public static partial void GraphicsSystem_Destroy(MGG_GraphicsSystem* system)
    {
    }

    public static partial MGG_GraphicsAdapter* GraphicsAdapter_Get(MGG_GraphicsSystem* system, int index) => index == 0 ? (MGG_GraphicsAdapter*)1 : null;

    public static partial void GraphicsAdapter_GetInfo(MGG_GraphicsAdapter* adapter, out MGG_GraphicsAdaptor_Info info)
    {
        info = default;
        info.DeviceName = DeviceName;
        info.Description = Description;
        SetDisplayMode(ref info.CurrentDisplayMode);
        info.DisplayModes = (MGG_DisplayMode*)NativeMemory.Alloc((nuint)sizeof(MGG_DisplayMode));
        SetDisplayMode(ref *info.DisplayModes);
        info.DisplayModeCount = 1;
    }

    public static partial MGG_GraphicsDevice* GraphicsDevice_Create(MGG_GraphicsSystem* system, MGG_GraphicsAdapter* adapter) => (MGG_GraphicsDevice*)1;

    public static partial void GraphicsDevice_Destroy(MGG_GraphicsDevice* device)
    {
    }

    public static partial void GraphicsDevice_GetCaps(MGG_GraphicsDevice* device, out MGG_GraphicsDevice_Caps caps)
    {
        caps = default;
        caps.MaxTextureSlots = 16;
        caps.MaxVertexBufferSlots = 8;
        caps.MaxVertexTextureSlots = 8;
        caps.ShaderProfile = ShaderProfileVulkan;
        caps.MaxTextureAnisotropy = 16;
        caps.MaxMultiSampleCount = 4;
        caps.SupportsNonPowerOfTwo = true;
        caps.SupportsTextureFilterAnisotropic = true;
        caps.SupportsDepth24 = true;
        caps.SupportsPackedDepthStencil = true;
        caps.SupportsTextureMaxLevel = true;
        caps.SupportsDxt1 = true;
        caps.SupportsS3tc = true;
        caps.SupportsSRgb = true;
        caps.SupportsDepthClamp = true;
        caps.SupportsTextureArrays = true;
        caps.SupportsVertexTextures = true;
        caps.SupportsFloatTextures = true;
        caps.SupportsHalfFloatTextures = true;
        caps.SupportsNormalized = true;
        caps.SupportsInstancing = true;
        caps.SupportsBaseIndexInstancing = true;
        caps.SupportsSeparateBlendStates = true;
    }

    public static partial int GraphicsDevice_GetBackBufferMultiSampleCount(MGG_GraphicsDevice* device) => 0;

    public static partial byte GraphicsDevice_ResizeSwapchain(MGG_GraphicsDevice* device, nint nativeWindowHandle, int width, int height, SurfaceFormat color, DepthFormat depth,
        int multiSampleCount, int syncInterval)
    {
        WebStream.Write(Op.ResizeSwapchain, width, height, (int)depth);
        return 1;
    }

    public static partial int GraphicsDevice_BeginFrame(MGG_GraphicsDevice* device)
    {
        WebStream.Write(Op.BeginFrame);
        return 0;
    }

    public static partial void GraphicsDevice_Clear(MGG_GraphicsDevice* device, ClearOptions options, ref Vector4 color, float depth, int stencil)
    {
        WebStream.Write(Op.Clear, [(int)options, WebStream.Bits(color.X), WebStream.Bits(color.Y), WebStream.Bits(color.Z), WebStream.Bits(color.W), WebStream.Bits(depth), stencil]);
    }

    public static partial void GraphicsDevice_Present(MGG_GraphicsDevice* device, int currentFrame, int syncInterval) => WebStream.EndFrame();

    public static partial void GraphicsDevice_SetBlendState(MGG_GraphicsDevice* device, MGG_BlendState* state, float factorR, float factorG, float factorB, float factorA)
    {
        WebStream.Write(Op.SetBlendState, [Id(state), WebStream.Bits(factorR), WebStream.Bits(factorG), WebStream.Bits(factorB), WebStream.Bits(factorA)]);
    }

    public static partial void GraphicsDevice_SetDepthStencilState(MGG_GraphicsDevice* device, MGG_DepthStencilState* state) => WebStream.Write(Op.SetDepthStencilState, Id(state));

    public static partial void GraphicsDevice_SetRasterizerState(MGG_GraphicsDevice* device, MGG_RasterizerState* state) => WebStream.Write(Op.SetRasterizerState, Id(state));

    public static partial void GraphicsDevice_GetTitleSafeArea(ref int x, ref int y, ref int width, ref int height)
    {
    }

    public static partial void GraphicsDevice_SetViewport(MGG_GraphicsDevice* device, int x, int y, int width, int height, float minDepth, float maxDepth)
    {
        WebStream.Write(Op.SetViewport, [x, y, width, height, WebStream.Bits(minDepth), WebStream.Bits(maxDepth)]);
    }

    public static partial void GraphicsDevice_SetScissorRectangle(MGG_GraphicsDevice* device, int x, int y, int width, int height) => WebStream.Write(Op.SetScissor, x, y, width, height);

    public static partial void GraphicsDevice_SetRenderTargets(MGG_GraphicsDevice* device, MGG_Texture** targets, int* arraySlices, int count)
    {
        Span<int> values = stackalloc int[1 + count * 2];
        values[0] = count;
        for (var i = 0; i < count; i++)
        {
            values[1 + i * 2] = Id(targets[i]);
            values[2 + i * 2] = arraySlices[i];
        }

        WebStream.Write(Op.SetRenderTargets, values);
    }

    public static partial void GraphicsDevice_SetConstantBuffer(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_Buffer* buffer) => WebStream.Write(Op.SetConstantBuffer, (int)stage, slot, Id(buffer));

    public static partial void GraphicsDevice_SetTexture(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_Texture* texture) => WebStream.Write(Op.SetTexture, (int)stage, slot, Id(texture));

    public static partial void GraphicsDevice_SetSamplerState(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_SamplerState* state) => WebStream.Write(Op.SetSampler, (int)stage, slot, Id(state));

    public static partial void GraphicsDevice_SetIndexBuffer(MGG_GraphicsDevice* device, IndexElementSize size, MGG_Buffer* buffer) => WebStream.Write(Op.SetIndexBuffer, (int)size, Id(buffer));

    public static partial void GraphicsDevice_SetVertexBuffer(MGG_GraphicsDevice* device, int slot, MGG_Buffer* buffer, int vertexOffset) => WebStream.Write(Op.SetVertexBuffer, slot, Id(buffer), vertexOffset);

    public static partial void GraphicsDevice_SetShader(MGG_GraphicsDevice* device, ShaderStage stage, MGG_Shader* shader) => WebStream.Write(Op.SetShader, (int)stage, Id(shader));

    public static partial void GraphicsDevice_SetInputLayout(MGG_GraphicsDevice* device, MGG_InputLayout* layout) => WebStream.Write(Op.SetInputLayout, Id(layout));

    public static partial void GraphicsDevice_Draw(MGG_GraphicsDevice* device, PrimitiveType primitiveType, int vertexStart, int vertexCount) => WebStream.Write(Op.Draw, (int)primitiveType, vertexStart, vertexCount);

    public static partial void GraphicsDevice_DrawIndexed(MGG_GraphicsDevice* device, PrimitiveType primitiveType, int primitiveCount, int indexStart, int vertexStart)
    {
        WebStream.Write(Op.DrawIndexed, (int)primitiveType, primitiveCount, indexStart, vertexStart);
    }

    public static partial void GraphicsDevice_DrawIndexedInstanced(MGG_GraphicsDevice* device, PrimitiveType primitiveType, int primitiveCount, int indexStart, int vertexStart, int baseInstance, int instanceCount)
    {
        WebStream.Write(Op.DrawIndexedInstanced, [(int)primitiveType, primitiveCount, indexStart, vertexStart, baseInstance, instanceCount]);
    }

    public static partial void GraphicsDevice_ResolveRenderTargets(MGG_GraphicsDevice* device) => WebStream.Write(Op.Resolve);

    public static partial void GraphicsDevice_GetBackBufferData(MGG_GraphicsDevice* device, int x, int y, int width, int height, nint data, int count, int dataBytes)
    {
        WebStream.WaitForRead(Op.BackBufferGetData, [x, y, width, height, (int)data, count * dataBytes]);
    }

    public static partial MGG_BlendState* BlendState_Create(MGG_GraphicsDevice* device, MGG_BlendState_Info* infos)
    {
        const int Targets = 4;
        const int Fields = 7;
        var id = WebStream.NewHandle();
        Span<int> values = stackalloc int[1 + Targets * Fields];
        values[0] = id;
        for (var i = 0; i < Targets; i++)
        {
            var offset = 1 + i * Fields;
            values[offset] = (int)infos[i].colorSourceBlend;
            values[offset + 1] = (int)infos[i].colorDestBlend;
            values[offset + 2] = (int)infos[i].colorBlendFunc;
            values[offset + 3] = (int)infos[i].alphaSourceBlend;
            values[offset + 4] = (int)infos[i].alphaDestBlend;
            values[offset + 5] = (int)infos[i].alphaBlendFunc;
            values[offset + 6] = (int)infos[i].colorWriteChannels;
        }

        WebStream.Write(Op.CreateBlendState, values);
        return (MGG_BlendState*)id;
    }

    public static partial void BlendState_Destroy(MGG_GraphicsDevice* device, MGG_BlendState* state) => WebStream.Write(Op.DestroyState, Id(state));

    public static partial MGG_DepthStencilState* DepthStencilState_Create(MGG_GraphicsDevice* device, MGG_DepthStencilState_Info* info)
    {
        var id = WebStream.NewHandle();
        WebStream.Write(Op.CreateDepthStencilState,
        [
            id, info->depthBufferEnable ? 1 : 0, info->depthBufferWriteEnable ? 1 : 0, (int)info->depthBufferFunction, info->referenceStencil, info->stencilEnable ? 1 : 0,
            info->stencilMask, info->stencilWriteMask, (int)info->stencilFunction, (int)info->stencilDepthBufferFail, (int)info->stencilFail, (int)info->stencilPass,
            info->twoSidedStencilMode ? 1 : 0, (int)info->counterClockwiseStencilFunction, (int)info->counterClockwiseStencilDepthBufferFail,
            (int)info->counterClockwiseStencilFail, (int)info->counterClockwiseStencilPass,
        ]);
        return (MGG_DepthStencilState*)id;
    }

    public static partial void DepthStencilState_Destroy(MGG_GraphicsDevice* device, MGG_DepthStencilState* state) => WebStream.Write(Op.DestroyState, Id(state));

    public static partial MGG_RasterizerState* RasterizerState_Create(MGG_GraphicsDevice* device, MGG_RasterizerState_Info* info)
    {
        var id = WebStream.NewHandle();
        WebStream.Write(Op.CreateRasterizerState,
        [
            id, (int)info->fillMode, (int)info->cullMode, info->scissorTestEnable ? 1 : 0, info->depthClipEnable ? 1 : 0, WebStream.Bits(info->depthBias), WebStream.Bits(info->slopeScaleDepthBias),
        ]);
        return (MGG_RasterizerState*)id;
    }

    public static partial void RasterizerState_Destroy(MGG_GraphicsDevice* device, MGG_RasterizerState* state) => WebStream.Write(Op.DestroyState, Id(state));

    public static partial MGG_SamplerState* SamplerState_Create(MGG_GraphicsDevice* device, MGG_SamplerState_Info* info)
    {
        var id = WebStream.NewHandle();
        WebStream.Write(Op.CreateSampler,
        [
            id, (int)info->AddressU, (int)info->AddressV, (int)info->AddressW, (int)info->Filter, (int)info->FilterMode, info->MaximumAnisotropy, info->MaxMipLevel,
            (int)info->ComparisonFunction,
        ]);
        return (MGG_SamplerState*)id;
    }

    public static partial void SamplerState_Destroy(MGG_GraphicsDevice* device, MGG_SamplerState* state) => WebStream.Write(Op.DestroyState, Id(state));

    public static partial MGG_Buffer* Buffer_Create(MGG_GraphicsDevice* device, BufferType type, bool dynamic, int sizeInBytes)
    {
        var id = WebStream.NewHandle();
        WebStream.Write(Op.CreateBuffer, id, (int)type, dynamic ? 1 : 0, sizeInBytes);
        return (MGG_Buffer*)id;
    }

    public static partial void Buffer_Destroy(MGG_GraphicsDevice* device, MGG_Buffer* buffer) => WebStream.Write(Op.DestroyBuffer, Id(buffer));

    private const int WriteOverwrite = 0;
    private const int WriteDiscard = 1;
    private const int WriteNoOverwrite = 2;

    private static void WriteBuffer(int id, int offset, int mode, byte* data, int elementCount, int vertexStride, int elementSize)
    {
        if (elementCount <= 1 || vertexStride == elementSize || vertexStride == 0)
        {
            WebStream.WriteData(Op.BufferSetData, [id, offset, mode], data, elementCount * elementSize);
            return;
        }

        for (var i = 0; i < elementCount; i++)
        {
            WebStream.WriteData(Op.BufferSetData, [id, offset + i * vertexStride, i == 0 ? mode : WriteNoOverwrite], data + i * elementSize, elementSize);
        }
    }

    public static partial void Buffer_SetData(MGG_GraphicsDevice* device, ref MGG_Buffer* buffer, int offset, byte* data, int elementCount, int vertexStride, int elementSizeInBytes, bool discard)
    {
        WriteBuffer(Id(buffer), offset, discard ? WriteDiscard : WriteOverwrite, data, elementCount, vertexStride, elementSizeInBytes);
    }

    public static partial void Buffer_SetDataNoOverwrite(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int offset, byte* data, int elementCount, int vertexStride, int elementSizeInBytes)
    {
        WriteBuffer(Id(buffer), offset, WriteNoOverwrite, data, elementCount, vertexStride, elementSizeInBytes);
    }

    public static partial void Buffer_GetData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int offset, byte* data, int dataCount, int dataBytes, int dataStride)
    {
        if (dataStride == dataBytes || dataStride == 0)
        {
            WebStream.WaitForRead(Op.BufferGetData, [Id(buffer), offset, (int)data, dataCount * dataBytes]);
            return;
        }

        for (var i = 0; i < dataCount; i++)
        {
            WebStream.WaitForRead(Op.BufferGetData, [Id(buffer), offset + i * dataStride, (int)(data + i * dataBytes), dataBytes]);
        }
    }

    private static MGG_Texture* NewTexture(int flags, TextureType type, SurfaceFormat format, int width, int height, int depth, int mipmaps, int slices, DepthFormat depthFormat, int multiSampleCount)
    {
        var id = WebStream.NewHandle();
        WebStream.Write(Op.CreateTexture, [id, flags, (int)type, (int)format, width, height, depth, mipmaps, slices, (int)depthFormat, multiSampleCount]);
        return (MGG_Texture*)id;
    }

    public static partial MGG_Texture* Texture_Create(MGG_GraphicsDevice* device, TextureType type, SurfaceFormat format, int width, int height, int depth, int mipmaps, int slices)
    {
        return NewTexture(0, type, format, width, height, depth, mipmaps, slices, DepthFormat.None, 0);
    }

    public static partial MGG_Texture* RenderTarget_Create(MGG_GraphicsDevice* device, TextureType type, SurfaceFormat format, int width, int height, int depth, int mipmaps, int slices,
        DepthFormat depthFormat, int multiSampleCount, RenderTargetUsage usage)
    {
        return NewTexture(TextureFlagRenderTarget, type, format, width, height, depth, mipmaps, slices, depthFormat, multiSampleCount);
    }

    public static partial MGG_Texture* RenderTarget_WrapNativeHandle(MGG_GraphicsDevice* device, nint nativeHandle, SurfaceFormat format, int width, int height, DepthFormat depthFormat, int multiSampleCount,
        byte externalPresentation) => null;

    public static partial void Texture_Destroy(MGG_GraphicsDevice* device, MGG_Texture* texture) => WebStream.Write(Op.DestroyTexture, Id(texture));

    public static partial void Texture_SetData(MGG_GraphicsDevice* device, MGG_Texture* texture, int level, int slice, int x, int y, int z, int width, int height, int depth, byte* data, int dataBytes)
    {
        WebStream.WriteData(Op.TextureSetData, [Id(texture), level, slice, x, y, z, width, height, depth], data, dataBytes);
    }

    public static partial void Texture_GetData(MGG_GraphicsDevice* device, MGG_Texture* texture, int level, int slice, int x, int y, int z, int width, int height, int depth, byte* data, int dataBytes)
    {
        WebStream.WaitForRead(Op.TextureGetData, [Id(texture), level, slice, x, y, z, width, height, depth, (int)data, dataBytes]);
    }

    public static partial MGG_InputLayout* InputLayout_Create(MGG_GraphicsDevice* device, MGG_Shader* vertexShader, int* strides, int streamCount, MGG_InputElement* elements, int elementCount)
    {
        const int ElementFields = 6;
        var id = WebStream.NewHandle();
        var values = new int[3 + streamCount + 1 + elementCount * ElementFields];
        values[0] = id;
        values[1] = Id(vertexShader);
        values[2] = streamCount;
        for (var i = 0; i < streamCount; i++)
        {
            values[3 + i] = strides[i];
        }

        var at = 3 + streamCount;
        values[at++] = elementCount;
        for (var i = 0; i < elementCount; i++)
        {
            values[at++] = (int)elements[i].SemanticUsage;
            values[at++] = (int)elements[i].SemanticIndex;
            values[at++] = (int)elements[i].VertexBufferSlot;
            values[at++] = (int)elements[i].Format;
            values[at++] = (int)elements[i].AlignedByteOffset;
            values[at++] = (int)elements[i].InstanceDataStepRate;
        }

        WebStream.Write(Op.CreateInputLayout, values);
        return (MGG_InputLayout*)id;
    }

    public static partial void InputLayout_Destroy(MGG_GraphicsDevice* device, MGG_InputLayout* layout) => WebStream.Write(Op.DestroyInputLayout, Id(layout));

    public static partial MGG_Shader* Shader_Create(MGG_GraphicsDevice* device, ShaderStage stage, byte* bytecode, int sizeInBytes)
    {
        var id = WebStream.NewHandle();
        WebStream.WriteData(Op.CreateShader, [id, (int)stage], bytecode, sizeInBytes);
        return (MGG_Shader*)id;
    }

    public static partial void Shader_Destroy(MGG_GraphicsDevice* device, MGG_Shader* shader) => WebStream.Write(Op.DestroyShader, Id(shader));

    public static partial MGG_OcclusionQuery* OcclusionQuery_Create(MGG_GraphicsDevice* device) => (MGG_OcclusionQuery*)WebStream.NewHandle();

    public static partial void OcclusionQuery_Destroy(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
    {
    }

    public static partial void OcclusionQuery_Begin(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
    {
    }

    public static partial void OcclusionQuery_End(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
    {
    }

    public static partial byte OcclusionQuery_GetResult(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query, out int pixelCount)
    {
        pixelCount = 1;
        return 1;
    }

    public static partial void GraphicsDevice_GetNativeHandles(MGG_GraphicsDevice* device, out MGP_NativeGraphicsHandles handles) => handles = default;

    private static partial int Etervale_RuntimeVersion() => EtervaleRuntimeVersion;

    public static partial void Buffer_SetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int offset, byte* data, int dataBytes)
    {
        WebStream.WriteData(Op.BufferSetData, [Id(buffer), offset, WriteOverwrite], data, dataBytes);
    }

    public static partial void Buffer_GetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int offset, byte* data, int dataBytes)
    {
        WebStream.WaitForRead(Op.BufferGetData, [Id(buffer), offset, (int)data, dataBytes]);
    }

    public static partial MGG_Texture* Texture_CreateStorage(MGG_GraphicsDevice* device, TextureType type, SurfaceFormat format, int width, int height, int depth, int mipmaps, int slices)
    {
        return NewTexture(TextureFlagStorage, type, format, width, height, depth, mipmaps, slices, DepthFormat.None, 0);
    }

    public static partial void GraphicsDevice_SetShaderImage(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_Texture* texture, int mip) => WebStream.Write(Op.SetShaderImage, (int)stage, slot, Id(texture), mip);

    public static partial void GraphicsDevice_Dispatch(MGG_GraphicsDevice* device, int groupsX, int groupsY, int groupsZ) => WebStream.Write(Op.Dispatch, groupsX, groupsY, groupsZ);

    public static partial void GraphicsDevice_DispatchIndirect(MGG_GraphicsDevice* device, MGG_Buffer* arguments, int byteOffset) => WebStream.Write(Op.DispatchIndirect, Id(arguments), byteOffset);

    public static partial void GraphicsDevice_SetShaderBuffer(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_Buffer* buffer, byte writable) => WebStream.Write(Op.SetShaderBuffer, (int)stage, slot, Id(buffer), writable);

    // Timestamps, frame capture and pipeline statistics are diagnostics the renderer treats as absent when these fail.
    public static partial nint TimestampPool_Create(MGG_GraphicsDevice* device, uint count) => 0;

    public static partial void TimestampPool_Destroy(MGG_GraphicsDevice* device, nint pool)
    {
    }

    public static partial byte TimestampPool_Reset(MGG_GraphicsDevice* device, nint pool, uint first, uint count) => 0;

    public static partial void TimestampPool_Write(MGG_GraphicsDevice* device, nint pool, uint index)
    {
    }

    public static partial byte TimestampPool_Read(MGG_GraphicsDevice* device, nint pool, uint first, uint count, ulong* ticks) => 0;

    public static partial float TimestampPool_GetPeriod(MGG_GraphicsDevice* device) => 0f;

    public static partial byte FrameCapture_Start(MGG_GraphicsDevice* device, int downscale, int maxQueued) => 0;

    public static partial void FrameCapture_Stop(MGG_GraphicsDevice* device)
    {
    }

    public static partial byte FrameCapture_Pop(MGG_GraphicsDevice* device, byte* data, int dataBytes, int* width, int* height, ulong* frame, byte* drawn) => 0;

    public static partial ulong FrameCapture_Dropped(MGG_GraphicsDevice* device) => 0;

    public static partial int GraphicsDevice_GetPipelineStatistics(MGG_GraphicsDevice* device, int compute, byte* buffer, int capacity) => 0;
}
