using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Gameplay.UI.Rendering;

public sealed class HudIconCache : IDisposable
{
    private sealed record PreparedIcon(string Key, DecodedPristonImage Image);

    private readonly GraphicsDevice _device;
    private readonly IClientLog _log;
    private readonly Task<Dictionary<string, string>> _index;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _requested = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pending = new();
    private readonly List<Task<PreparedIcon>> _decoding = [];
    private bool _disposed;
    private bool _failed;

    public HudIconCache(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        _device = device;
        _log = log;
        _index = Task.Run(() => ItemIconFiles.Index(assetRoot));
        _ = _index.ContinueWith(task => log.Error("HUD item icon index failed", task.Exception),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public Texture2D Find(string key) => _textures.GetValueOrDefault(key);
    public bool IsIdle => _pending.Count == 0 && _decoding.Count == 0;
    public bool Failed => _failed || _index.IsFaulted;

    // A pack, a full shop and a full warehouse at once, with room to spare.
    private const int MaxIcons = 512;
    // Icons decoding at once; every finished one uploads on the next update (each is at most 256 x 256).
    private const int MaxDecodes = 4;

    public void Request(IEnumerable<string> keys)
    {
        // Call only when the required set changes; discard stale queued work and GPU resources.
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 80 || current.Count >= MaxIcons)
            {
                continue;
            }

            current.Add(key);
            if (_requested.Add(key))
            {
                _pending.Enqueue(key);
            }
        }

        foreach (var key in _textures.Keys.ToArray())
        {
            if (!current.Contains(key))
            {
                _textures[key].Dispose();
                _textures.Remove(key);
            }
        }

        _requested.IntersectWith(current);
        var pendingCount = _pending.Count;
        for (var index = 0; index < pendingCount; index++)
        {
            var key = _pending.Dequeue();
            if (current.Contains(key))
            {
                _pending.Enqueue(key);
            }
        }
    }

    public void Update()
    {
        if (_disposed || !_index.IsCompletedSuccessfully)
        {
            return;
        }

        for (var index = _decoding.Count - 1; index >= 0; index--)
        {
            var decode = _decoding[index];
            if (!decode.IsCompleted)
            {
                continue;
            }

            _decoding.RemoveAt(index);
            if (decode.IsCompletedSuccessfully)
            {
                Upload(decode.Result);
            }
            else
            {
                _failed = true;
            }
        }

        while (_decoding.Count < MaxDecodes && _pending.TryDequeue(out var key))
        {
            var paths = _index.Result;
            var decode = Task.Run(() => Decode(key, paths));
            _ = decode.ContinueWith(task => _log.Error($"HUD item icon failed: {key}", task.Exception),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            _decoding.Add(decode);
        }
    }

    private void Upload(PreparedIcon prepared)
    {
        if (!prepared.Image.IsValid || !_requested.Contains(prepared.Key))
        {
            return;
        }

        var image = prepared.Image;
        var texture = new Texture2D(_device, image.Width, image.Height);
        try
        {
            texture.SetData(image.Pixels);
            if (_textures.Remove(prepared.Key, out var previous))
            {
                previous.Dispose();
            }

            _textures.Add(prepared.Key, texture);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private PreparedIcon Decode(string key, Dictionary<string, string> paths)
    {
        if (!paths.TryGetValue(key, out var path))
        {
            throw new FileNotFoundException($"HUD item icon is missing for key '{key}'.", key);
        }

        var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), ".bmp", bmpCoverage: true);
        if (!image.IsValid || image.Width > 256 || image.Height > 256)
        {
            throw new InvalidDataException($"Invalid HUD icon dimensions: {path}");
        }

        PristonUiPixels.Prepare(image, PristonUiColorKey.Black);

        return new PreparedIcon(key, image);
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _pending.Clear();
        _decoding.Clear();
    }
}
