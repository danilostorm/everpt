using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Gameplay.UI.Rendering;

public sealed record HudRegion(int Texture, int X, int Y, int Width, int Height, int Border)
{
    public Rectangle Bounds => new(X, Y, Width, Height);
}

public sealed record HudGaugeDefinition(string Frame, string Region);
public sealed record HudGauge(HudRegion Fill, Vector2 Offset, Vector2 Size);
public sealed record HudAtlasManifest(int Version, string[] Textures, Dictionary<string, HudRegion> Regions,
    int[] MagentaKeyTextures, Dictionary<string, HudGaugeDefinition> Gauges);

public sealed class HudAtlas : IDisposable
{
    private sealed record Prepared(HudAtlasManifest Manifest, DecodedPristonImage[] Images, Dictionary<string, HudGauge> Gauges);

    private readonly GraphicsDevice _device;
    private readonly IClientLog _log;
    private Task<Prepared> _preparation;
    private Prepared _prepared;
    private Texture2D[] _textures = [];
    private int _textureIndex;
    private int _row;
    private bool _disposed;

    public HudAtlas(GraphicsDevice device, string assetRoot, IClientLog log)
        : this(device, log)
    {
        _preparation = Task.Run(() => Prepare(assetRoot));
        // Observe abandoned preparation faults even when a screen is closed before decoding finishes.
        _ = _preparation.ContinueWith(task => log.Error("HUD atlas preparation failed", task.Exception),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private HudAtlas(GraphicsDevice device, IClientLog log)
    {
        _device = device;
        _log = log;
    }

    public static HudAtlas LoadBootstrap(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        var atlas = new HudAtlas(device, log);
        atlas._prepared = Prepare(assetRoot);
        atlas._textures = new Texture2D[atlas._prepared.Images.Length];
        while (!atlas.IsReady && !atlas.Failed)
        {
            atlas.Update();
        }

        if (atlas.Failed)
        {
            atlas.Dispose();
            throw new InvalidOperationException("Interface resources could not be initialized.");
        }

        return atlas;
    }

    public bool IsReady { get; private set; }
    public bool Failed { get; private set; }
    public long TextureBytes { get; private set; }

    public HudRegion Region(string name) => _prepared.Manifest.Regions[name];
    public Texture2D Texture(int index) => _textures[index];
    public HudGauge Gauge(string name) => _prepared.Gauges[name];

    public void Update()
    {
        if (_disposed || IsReady || Failed)
        {
            return;
        }

        if (_prepared is null)
        {
            if (!_preparation.IsCompleted)
            {
                return;
            }

            if (!_preparation.IsCompletedSuccessfully)
            {
                Failed = true;
                _preparation = null;
                return;
            }

            _prepared = _preparation.Result;
            _preparation = null;
            _textures = new Texture2D[_prepared.Images.Length];
        }

        try
        {
            var image = _prepared.Images[_textureIndex];
            var texture = _textures[_textureIndex];
            if (texture is null)
            {
                texture = new Texture2D(_device, image.Width, image.Height, false, SurfaceFormat.Color);
                _textures[_textureIndex] = texture;
                TextureBytes += (long)image.Width * image.Height * 4;
                return;
            }

            // At most 768 KiB per update for the shipped 1536-wide atlases; no file access here.
            var rows = Math.Min(128, image.Height - _row);
            texture.SetData(0, new Rectangle(0, _row, image.Width, rows), image.Pixels, _row * image.Width, rows * image.Width);
            _row += rows;
            if (_row == image.Height)
            {
                _prepared.Images[_textureIndex] = DecodedPristonImage.Empty;
                _textureIndex++;
                _row = 0;
                IsReady = _textureIndex == _textures.Length;
            }
        }
        catch (Exception exception)
        {
            Failed = true;
            _log.Error("HUD atlas upload failed", exception);
            ReleaseTextures();
        }
    }

    private static Prepared Prepare(string assetRoot)
    {
        var root = Path.Combine(assetRoot, "Hud");
        var options = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        };
        var manifest = JsonSerializer.Deserialize<HudAtlasManifest>(
            AssetFiles.ReadAllBytes(Path.Combine(root, "Manifests", "atlas.json")), options);
        if (manifest is null || manifest.Version != 1 || manifest.Textures is null
            || manifest.Textures.Length is < 1 or > 4 || manifest.Regions is null || manifest.MagentaKeyTextures is null
            || manifest.Gauges is null || manifest.Gauges.Count > 4)
        {
            throw new InvalidDataException("Unsupported HUD atlas manifest.");
        }

        var images = new DecodedPristonImage[manifest.Textures.Length];
        for (var index = 0; index < images.Length; index++)
        {
            var name = manifest.Textures[index];
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name.Contains('\\'))
            {
                throw new InvalidDataException("HUD texture must be a filename within Hud/Textures.");
            }

            var bytes = AssetFiles.ReadAllBytes(Path.Combine(root, "Textures", name));
            var image = PristonImageDecoder.Decode(bytes, Path.GetExtension(name));
            if (!image.IsValid || image.Width > 2048 || image.Height > 2048)
            {
                throw new InvalidDataException($"Invalid HUD texture: {name}");
            }

            var colorKey = manifest.MagentaKeyTextures.Contains(index) ? PristonUiColorKey.Magenta : PristonUiColorKey.None;
            PristonUiPixels.Prepare(image, colorKey);

            images[index] = image;
        }

        foreach (var region in manifest.Regions.Values)
        {
            if (region.Texture < 0 || region.Texture >= images.Length || region.X < 0 || region.Y < 0
                || region.Width <= 0 || region.Height <= 0 || region.Border < 0
                || region.Border > Math.Min(region.Width, region.Height) / 2
                || (long)region.X + region.Width > images[region.Texture].Width
                || (long)region.Y + region.Height > images[region.Texture].Height)
            {
                throw new InvalidDataException("HUD atlas region is outside its texture.");
            }
        }

        var preparedImages = images.ToList();
        var gauges = new Dictionary<string, HudGauge>(StringComparer.Ordinal);
        foreach (var (name, definition) in manifest.Gauges)
        {
            if (!manifest.Regions.TryGetValue(definition.Frame, out var frame)
                || !manifest.Regions.TryGetValue(definition.Region, out var region)
                || frame.Texture != region.Texture || !frame.Bounds.Contains(region.Bounds))
            {
                throw new InvalidDataException("HUD gauge must be contained in its frame.");
            }

            var fill = HudGaugePixels.Separate(images[region.Texture], region.Bounds);
            var fillRegion = new HudRegion(preparedImages.Count, 0, 0, fill.Width, fill.Height, 0);
            var offset = new Vector2((float)(region.X - frame.X) / frame.Width, (float)(region.Y - frame.Y) / frame.Height);
            var size = new Vector2((float)region.Width / frame.Width, (float)region.Height / frame.Height);
            gauges.Add(name, new HudGauge(fillRegion, offset, size));
            preparedImages.Add(fill);
        }

        // Discard unused margins before upload, retaining every declared region and the exact source pixels.
        for (var index = 0; index < images.Length; index++)
        {
            var entries = manifest.Regions.Where(entry => entry.Value.Texture == index).ToArray();
            if (entries.Length == 0)
            {
                throw new InvalidDataException("HUD texture has no regions.");
            }

            var crop = entries[0].Value.Bounds;
            foreach (var entry in entries)
            {
                crop = Rectangle.Union(crop, entry.Value.Bounds);
            }

            preparedImages[index] = HudGaugePixels.Crop(images[index], crop);
            foreach (var (name, region) in entries)
            {
                manifest.Regions[name] = region with { X = region.X - crop.X, Y = region.Y - crop.Y };
            }
        }

        return new Prepared(manifest, preparedImages.ToArray(), gauges);
    }

    private void ReleaseTextures()
    {
        foreach (var texture in _textures)
        {
            texture?.Dispose();
        }

        _textures = [];
        TextureBytes = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseTextures();
        _prepared = null;
        _preparation = null;
    }
}
