// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the exports the runtime fork (monogame/runtime) adds to the native runtime. Each group has a
// change document in monogame/issues. Etervale fork: the in-place uploads of issue 013 and this message's build commands.

using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGame.Interop;

internal static unsafe partial class MGG
{
    #region Runtime version (issue 002)

    /// <summary>
    /// The interface version of monogame/runtime this framework is written for (MGVK_ETERVALE_RUNTIME_VERSION in
    /// MGG_Vulkan.cpp). Both change together, with every change of an exported function.
    /// </summary>
    internal const int EtervaleRuntimeVersion = 7;

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_Etervale_RuntimeVersion", ExactSpelling = true)]
    private static extern int Etervale_RuntimeVersion();

    /// <summary>
    /// Stops with instructions when the runtime library is not the fork built from the same sources: the official
    /// package has no version export, and a stale build of the fork reports another version.
    /// </summary>
    internal static void EnsureRuntimeVersion()
    {
        int version;
        try
        {
            version = Etervale_RuntimeVersion();
        }
        catch (EntryPointNotFoundException)
        {
            version = 0;
        }

        if (version != EtervaleRuntimeVersion)
        {
            throw new InvalidOperationException(
                $"The native runtime library (mgruntime) has interface version {version}, but this MonoGame.Framework needs " +
                $"version {EtervaleRuntimeVersion}: it is not the runtime fork built from the same sources. Rebuild it " +
                "with monogame/runtime/build-windows.ps1 (Windows) or monogame/runtime/build.sh (Linux, macOS); " +
                "see monogame/runtime/README.md.");
        }
    }

    #endregion

    #region In-place uploads (Etervale fork, issue 013)

    /// <summary>
    /// SetDataOptions.NoOverwrite on a dynamic buffer: writes in place, since the caller promises no recorded or
    /// in-flight draw reads the range (MGG_Buffer_SetData would rename the buffer and copy its old content back).
    /// </summary>
    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_Buffer_SetDataNoOverwrite", ExactSpelling = true)]
    public static extern void Buffer_SetDataNoOverwrite(
        MGG_GraphicsDevice* device,
        MGG_Buffer* buffer,
        int offset,
        byte* data,
        int elementCount,
        int vertexStride,
        int elementSizeInBytes);

    #endregion

    #region GPU timestamps (issue 004)

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_TimestampPool_Create", ExactSpelling = true)]
    public static extern nint TimestampPool_Create(MGG_GraphicsDevice* device, uint count);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_TimestampPool_Destroy", ExactSpelling = true)]
    public static extern void TimestampPool_Destroy(MGG_GraphicsDevice* device, nint pool);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_TimestampPool_Reset", ExactSpelling = true)]
    public static extern byte TimestampPool_Reset(MGG_GraphicsDevice* device, nint pool, uint first, uint count);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_TimestampPool_Write", ExactSpelling = true)]
    public static extern void TimestampPool_Write(MGG_GraphicsDevice* device, nint pool, uint index);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_TimestampPool_Read", ExactSpelling = true)]
    public static extern byte TimestampPool_Read(MGG_GraphicsDevice* device, nint pool, uint first, uint count, ulong* ticks);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_TimestampPool_GetPeriod", ExactSpelling = true)]
    public static extern float TimestampPool_GetPeriod(MGG_GraphicsDevice* device);

    #endregion

    #region Asynchronous frame capture (issue 003)

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_FrameCapture_Start", ExactSpelling = true)]
    public static extern byte FrameCapture_Start(MGG_GraphicsDevice* device, int downscale, int maxQueued);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_FrameCapture_Stop", ExactSpelling = true)]
    public static extern void FrameCapture_Stop(MGG_GraphicsDevice* device);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_FrameCapture_Pop", ExactSpelling = true)]
    public static extern byte FrameCapture_Pop(MGG_GraphicsDevice* device, byte* data, int dataBytes, int* width, int* height, ulong* frame, byte* drawn);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_FrameCapture_Dropped", ExactSpelling = true)]
    public static extern ulong FrameCapture_Dropped(MGG_GraphicsDevice* device);

    #endregion

    #region Compute and storage images (issue 005)

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_Texture_CreateStorage", ExactSpelling = true)]
    public static extern MGG_Texture* Texture_CreateStorage(MGG_GraphicsDevice* device, TextureType type, SurfaceFormat format, int width, int height, int depth, int mipmaps, int slices);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_GraphicsDevice_SetShaderImage", ExactSpelling = true)]
    public static extern void GraphicsDevice_SetShaderImage(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_Texture* texture, int mip);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_GraphicsDevice_Dispatch", ExactSpelling = true)]
    public static extern void GraphicsDevice_Dispatch(MGG_GraphicsDevice* device, int groupsX, int groupsY, int groupsZ);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_GraphicsDevice_DispatchIndirect", ExactSpelling = true)]
    public static extern void GraphicsDevice_DispatchIndirect(MGG_GraphicsDevice* device, MGG_Buffer* arguments, int byteOffset);

    #endregion

    #region Storage buffers (issue 006)

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_GraphicsDevice_SetShaderBuffer", ExactSpelling = true)]
    public static extern void GraphicsDevice_SetShaderBuffer(MGG_GraphicsDevice* device, ShaderStage stage, int slot, MGG_Buffer* buffer, byte writable);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_Buffer_SetStorageData", ExactSpelling = true)]
    public static extern void Buffer_SetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int offset, byte* data, int dataBytes);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_Buffer_GetStorageData", ExactSpelling = true)]
    public static extern void Buffer_GetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, int offset, byte* data, int dataBytes);

    #endregion

    #region Pipeline statistics (issue 012)

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGG_GraphicsDevice_GetPipelineStatistics", ExactSpelling = true)]
    public static extern int GraphicsDevice_GetPipelineStatistics(MGG_GraphicsDevice* device, int compute, byte* buffer, int capacity);

    #endregion
}
