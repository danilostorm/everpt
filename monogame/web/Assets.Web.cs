using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using MonoGame.Interop;
using System;

// Content opens through the same virtual file tree as the game data.
[SupportedOSPlatform("browser")]
internal static unsafe partial class MG
{
    private static readonly Dictionary<int, FileStream> OpenAssets = new();
    private static int _nextAsset = 1;

    public static partial byte AssetOpen(string assetname, out MG_Asset* file, out long length)
    {
        file = null;
        length = 0;
        if (!File.Exists(assetname))
        {
            return 0;
        }

        var stream = File.OpenRead(assetname);
        lock (OpenAssets)
        {
            var id = _nextAsset++;
            OpenAssets[id] = stream;
            file = (MG_Asset*)id;
        }

        length = stream.Length;
        return 1;
    }

    private static FileStream Asset(MG_Asset* file)
    {
        lock (OpenAssets)
        {
            return OpenAssets[(int)(nint)file];
        }
    }

    public static partial int AssetRead(MG_Asset* file, byte* buffer, long count) => Asset(file).Read(new Span<byte>(buffer, (int)count));

    public static partial long AssetSeek(MG_Asset* file, long offset, int origin) => Asset(file).Seek(offset, (SeekOrigin)origin);

    public static partial void AssetClose(MG_Asset* file)
    {
        lock (OpenAssets)
        {
            var id = (int)(nint)file;
            OpenAssets[id].Dispose();
            OpenAssets.Remove(id);
        }
    }
}
