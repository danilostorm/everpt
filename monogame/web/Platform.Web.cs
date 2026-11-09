using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using MonoGame.Framework.Utilities;

namespace MonoGame.Interop;

// The browser's window, input and file access: the single canvas is the window, and mgweb.js queues its input events.
[SupportedOSPlatform("browser")]
internal static unsafe partial class MGP
{
    private const nint WindowHandle = 1;

    public static partial MGP_Platform* Platform_Create(out GameRunBehavior behavior)
    {
        behavior = GameRunBehavior.Synchronous;
        WebStream.EnsureStarted();
        return (MGP_Platform*)1;
    }

    public static partial void Platform_Destroy(MGP_Platform* platform)
    {
    }

    public static partial void Platform_BeforeInitialize(MGP_Platform* platform)
    {
    }

    public static partial byte Platform_PollEvent(MGP_Platform* platform, out MGP_Event event_)
    {
        event_ = default;
        if (!WebStream.TryReadEvent(out var type, out var a, out var b, out var c, out var d))
        {
            return 0;
        }

        event_.Type = (EventType)type;
        switch (event_.Type)
        {
            case EventType.WindowResized:
                event_.Window.Window = WindowHandle;
                event_.Window.Data1 = a;
                event_.Window.Data2 = b;
                break;
            case EventType.KeyDown:
            case EventType.KeyUp:
            case EventType.TextInput:
                event_.Key.Window = WindowHandle;
                event_.Key.Key = (Keys)a;
                event_.Key.Character = (uint)b;
                break;
            case EventType.MouseMove:
                event_.MouseMove.Window = WindowHandle;
                event_.MouseMove.X = a;
                event_.MouseMove.Y = b;
                break;
            case EventType.MouseButtonDown:
            case EventType.MouseButtonUp:
                event_.MouseButton.Window = WindowHandle;
                event_.MouseButton.Button = (MouseButton)a;
                event_.MouseButton.X = b;
                event_.MouseButton.Y = c;
                break;
            case EventType.MouseWheel:
                event_.MouseWheel.Window = WindowHandle;
                event_.MouseWheel.Scroll = a;
                event_.MouseWheel.ScrollH = b;
                break;
        }

        return 1;
    }

    public static partial void Platform_StartRunLoop(MGP_Platform* platform)
    {
    }

    public static partial byte Platform_BeforeRun(MGP_Platform* platform) => 1;

    public static partial byte Platform_BeforeUpdate(MGP_Platform* platform) => 1;

    public static partial byte Platform_BeforeDraw(MGP_Platform* platform) => 1;

    private static partial nint _Platform_MakePath(string location, string path) => Marshal.StringToCoTaskMemUTF8(Path.Combine(location, path));

    private static partial void _Platform_Free(nint ptr) => Marshal.FreeCoTaskMem(ptr);

    public static partial MonoGamePlatform Platform_GetPlatform() => MonoGamePlatform.DesktopVK;

    public static partial GraphicsBackend Platform_GetGraphicsBackend() => GraphicsBackend.Vulkan;

    private static partial MGP_Window* _Window_Create(MGP_Platform* platform, ref int width, ref int height, string title, MGP_WindowCreateInfo* openGLCreateInfo) => (MGP_Window*)WindowHandle;

    private static partial byte _Window_CreateNativeWindow(MGP_Window* window, ref int width, ref int height, string title, MGP_WindowCreateInfo* windowCreateinfo) => 1;

    private static partial byte _Window_BeginRecreateNativeWindow(MGP_Window* window, ref int width, ref int height, string title, MGP_WindowCreateInfo* windowCreateInfo) => 1;

    public static partial void Window_Destroy(MGP_Window* window)
    {
    }

    public static partial void Window_DestroyNativeWindow(MGP_Window* window)
    {
    }

    public static partial void Window_FinalizeRecreateNativeWindow(MGP_Window* window)
    {
    }

    public static partial void Window_SetIconBitmap(MGP_Window* window, byte* icon, int length)
    {
    }

    public static partial nint Window_GetNativeHandle(MGP_Window* window) => WindowHandle;

    public static partial byte Window_GetAllowUserResizing(MGP_Window* window) => 1;

    public static partial void Window_SetAllowUserResizing(MGP_Window* window, byte allow)
    {
    }

    public static partial byte Window_GetIsBorderless(MGP_Window* window) => 1;

    public static partial void Window_SetIsBorderless(MGP_Window* window, byte borderless)
    {
    }

    public static partial void Window_SetTitle(MGP_Window* window, string title)
    {
    }

    public static partial void Window_Show(MGP_Window* window, byte show)
    {
    }

    public static partial void Window_GetPosition(MGP_Window* window, out int x, out int y)
    {
        x = 0;
        y = 0;
    }

    public static partial void Window_SetPosition(MGP_Window* window, int x, int y)
    {
    }

    public static partial byte Window_GetIsMaximized(MGP_Window* window) => 0;

    public static partial void Window_Maximize(MGP_Window* window)
    {
    }

    public static partial void Window_SetClientSize(MGP_Window* window, int width, int height)
    {
    }

    public static partial void Window_SetCursor(MGP_Window* window, MGP_Cursor* cursor)
    {
    }

    public static partial int Window_ShowMessageBox(MGP_Window* window, string title, string description, string buttons, int count)
    {
        Console.Error.WriteLine($"[MessageBox] {title}: {description}");
        return 0;
    }

    public static partial void Window_EnterFullScreen(MGP_Window* window, byte useHardwareModeSwitch)
    {
    }

    public static partial void Window_ExitFullScreen(MGP_Window* window)
    {
    }

    public static partial void Mouse_SetVisible(MGP_Platform* platform, byte visible)
    {
    }

    public static partial void Mouse_WarpPosition(MGP_Window* window, int x, int y)
    {
    }

    public static partial MGP_Cursor* Cursor_Create(SystemCursor cursor) => (MGP_Cursor*)1;

    public static partial MGP_Cursor* Cursor_CreateCustom(byte* rgba, int width, int height, int originx, int originy) => (MGP_Cursor*)1;

    public static partial void Cursor_Destroy(MGP_Cursor* cursor)
    {
    }

    public static partial int GamePad_GetMaxSupported() => 0;

    public static partial void GamePad_GetCaps(MGP_Platform* platform, int identifer, MGP_ControllerCaps* caps)
    {
    }

    public static partial byte GamePad_SetVibration(MGP_Platform* platform, int identifer, float leftMotor, float rightMotor, float leftTrigger, float rightTrigger) => 0;
}

[SupportedOSPlatform("browser")]
internal static unsafe partial class MGI
{
    public static partial void ReadRGBA(byte* data, int dataBytes, ProcessorType processors, out int width, out int height, out byte* rgba)
    {
        var bytes = new byte[dataBytes];
        Marshal.Copy((nint)data, bytes, 0, dataBytes);
        var image = StbImageSharp.ImageResult.FromMemory(bytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        width = image.Width;
        height = image.Height;
        rgba = (byte*)NativeMemory.Alloc((nuint)image.Data.Length);
        Marshal.Copy(image.Data, 0, (nint)rgba, image.Data.Length);
    }

    public static partial void FreeRGBA(byte* rgba) => NativeMemory.Free(rgba);

    public static partial void WriteJpg(byte* data, int dataBytes, int width, int height, int quality, out byte* jpg, out int jpgBytes)
    {
        jpg = null;
        jpgBytes = 0;
    }

    public static partial void WritePng(byte* data, int dataBytes, int width, int height, out byte* png, out int pngBytes)
    {
        png = null;
        pngBytes = 0;
    }
}
