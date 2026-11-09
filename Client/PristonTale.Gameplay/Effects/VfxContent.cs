using PristonTale.Assets;
#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Scripts;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Effects;

// The client's effect library: the definitions under <assetRoot>/Effect/Vfx are read, validated and their models parsed
// and textures decoded on a worker as the client starts; Compile then uploads and registers them on the game thread in
// slices of a time budget, so no frame waits for the disk.
public sealed class VfxContent : IVfxResources, IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ContentManager _content;
    private readonly PristonTextureLoader _textures;
    private readonly string _assetRoot;
    private readonly IClientLog _log;
    private readonly Task<Prepared> _preparation;
    private readonly Dictionary<string, PristonMeshEffect?> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BillboardSequence?> _readOnUse = new(StringComparer.OrdinalIgnoreCase);
    private Prepared? _prepared;
    private int _next;

    public VfxContent(GraphicsDevice device, ContentManager content, PristonTextureLoader textures, string assetRoot, IClientLog log)
    {
        _device = device;
        _content = content;
        _textures = textures;
        _assetRoot = assetRoot;
        _log = log;
        Builtins = new VfxBuiltinTextures(device);
        Library = new VfxLibrary(this, textures.White, Builtins);
        _preparation = Task.Run(Prepare);
    }

    public VfxLibrary Library { get; }
    public VfxBuiltinTextures Builtins { get; }

    // True once every definition is registered.
    public bool Ready { get; private set; }

    // Registers prepared definitions until the budget is spent; returns whether all are registered. Never blocks.
    public bool Compile(TimeSpan budget)
    {
        if (Ready)
        {
            return true;
        }

        if (_prepared is null)
        {
            if (!_preparation.IsCompleted)
            {
                return false;
            }

            if (!_preparation.IsCompletedSuccessfully)
            {
                _log.Warn($"[Effects] effect definitions could not be prepared: {_preparation.Exception?.GetBaseException().Message}");
                Ready = true;
                return true;
            }

            _prepared = _preparation.Result;
            foreach (var error in _prepared.Catalog.Errors)
            {
                _log.Warn($"[Effects] {error}");
            }
        }

        var clock = Stopwatch.StartNew();
        var definitions = _prepared.Catalog.Definitions;
        while (_next < definitions.Count && clock.Elapsed < budget)
        {
            Library.Register(definitions[_next++]);
        }

        if (_next >= definitions.Count)
        {
            Ready = true;
            _log.Info($"[Effects] {Library.Count} effects from {_prepared.Catalog.Files} files.");
        }

        return Ready;
    }

    // Loading screens, probes and tools: wait for the worker and register everything now.
    public void CompileAll()
    {
        _preparation.Wait();
        Compile(TimeSpan.MaxValue);
    }

    Texture2D? IVfxResources.Texture(string path)
    {
        return AssetFiles.Exists(Path.Combine(_assetRoot, path)) ? _textures.LoadTexture(_assetRoot, path) : null;
    }

    // A static model outside the effect definitions (a thrown weapon), read on first use; null when it cannot be read.
    public PristonMeshEffect? LoadModel(string path)
    {
        var key = $"{path}||1";
        if (_models.TryGetValue(key, out var cached))
        {
            return cached;
        }

        PristonMeshEffect? model = null;
        try
        {
            var mesh = new PristonSmdReader().ReadModel(Path.Combine(_assetRoot, path));
            model = new PristonMeshEffect(_device, _content, _textures, mesh, mesh, 1);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            _log.Warn($"[Effects] model {path} could not be built: {exception.Message}");
        }

        _models[key] = model;
        return model;
    }

    PristonMeshEffect? IVfxResources.Model(string path, string skeleton, int frames)
    {
        var key = $"{path}|{skeleton}|{frames}";
        if (_models.TryGetValue(key, out var cached))
        {
            return cached;
        }

        PristonMeshEffect? model = null;
        if (_prepared is not null && _prepared.Models.TryGetValue(ModelKey(path, skeleton), out var parsed) && parsed.Mesh is not null)
        {
            try
            {
                model = new PristonMeshEffect(_device, _content, _textures, parsed.Mesh, parsed.Skeleton ?? parsed.Mesh, frames);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
            {
                _log.Warn($"[Effects] effect model {path} could not be built: {exception.Message}");
            }
        }

        _models[key] = model;
        return model;
    }

    private Prepared Prepare()
    {
        var catalog = VfxCatalog.Load(_assetRoot);
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new ConcurrentDictionary<string, ParsedModel>(StringComparer.OrdinalIgnoreCase);
        var animations = new Dictionary<string, BillboardSequence?>(StringComparer.OrdinalIgnoreCase);
        var reader = new PristonSmdReader();
        var decoding = new List<Task>();
        var effectTextures = EffectTextureIndex();
        foreach (var definition in catalog.Definitions)
        {
            foreach (var texture in VfxCatalog.Textures(definition))
            {
                QueueTexture(texture, textures, decoding);
            }

            foreach (var component in definition.Components)
            {
                if (component.Kind == VfxComponentKind.Model)
                {
                    QueueModel(reader, component.Model, component.Skeleton, models, decoding, effectTextures);
                }
            }

            foreach (var part in definition.Parts)
            {
                if (part.Shape == VfxPartShape.Model)
                {
                    QueueModel(reader, part.Model, part.Skeleton, models, decoding, effectTextures);
                }
                else if (part.Animation.Length > 0 && !animations.ContainsKey(part.Animation))
                {
                    var animation = ReadAnimation(part.Animation);
                    animations[part.Animation] = animation;
                    foreach (var image in animation?.Images ?? [])
                    {
                        QueueTexture(image, textures, decoding);
                    }
                }
            }
        }

        Task.WaitAll(decoding.ToArray());
        return new Prepared(catalog, models, animations);
    }

    // Effects defined in code (ItemVfx) may name animations no catalog file uses, and may register before the catalog is
    // prepared: those are read on first use.
    BillboardSequence? IVfxResources.Animation(string name)
    {
        if (_prepared is not null && _prepared.Animations.TryGetValue(name, out var prepared))
        {
            return prepared;
        }

        if (!_readOnUse.TryGetValue(name, out var animation))
        {
            animation = ReadAnimation(name);
            _readOnUse[name] = animation;
        }

        return animation;
    }

    private BillboardSequence? ReadAnimation(string name)
    {
        try
        {
            return BillboardSequence.Read(name, path => AssetFiles.OpenRead(Path.Combine(_assetRoot, path)));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or UnauthorizedAccessException
            or OverflowException)
        {
            _log.Warn($"[Effects] animated billboard {name} could not be read: {exception.Message}");
            return null;
        }
    }

    private void QueueModel(PristonSmdReader reader, string path, string skeleton, ConcurrentDictionary<string, ParsedModel> models, List<Task> decoding,
        IReadOnlyDictionary<string, string> effectTextures)
    {
        var key = ModelKey(path, skeleton);
        if (models.ContainsKey(key))
        {
            return;
        }

        var parsed = Parse(reader, path, skeleton);
        if (parsed.Mesh is { } mesh)
        {
            parsed = parsed with { Mesh = Relink(mesh, effectTextures) };
        }

        models[key] = parsed;
        if (parsed.Mesh is not null)
        {
            decoding.Add(_textures.PrepareTexturesAsync(Path.GetDirectoryName(parsed.Mesh.Path) ?? ".", parsed.Mesh.Materials));
        }
    }

    private void QueueTexture(string texture, HashSet<string> textures, List<Task> decoding)
    {
        if (texture.Length > 0 && !texture.StartsWith(VfxBuiltinTextures.Prefix, StringComparison.OrdinalIgnoreCase) && textures.Add(texture)
            && AssetFiles.Exists(Path.Combine(_assetRoot, texture)))
        {
            decoding.Add(_textures.PrepareTextureAsync(_assetRoot, texture));
        }
    }

    private ParsedModel Parse(PristonSmdReader reader, string path, string skeleton)
    {
        try
        {
            var mesh = reader.ReadModel(Path.Combine(_assetRoot, path));
            PristonModel? bones = null;
            if (skeleton.Length > 0)
            {
                bones = reader.ReadModel(Path.Combine(_assetRoot, skeleton));
            }
            else
            {
                var sibling = Path.ChangeExtension(path, ".smb");
                if (AssetFiles.Exists(Path.Combine(_assetRoot, sibling)))
                {
                    bones = reader.ReadModel(Path.Combine(_assetRoot, sibling));
                }
            }

            return new ParsedModel(mesh, bones);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _log.Warn($"[Effects] effect model {path} could not be read: {exception.Message}");
            return new ParsedModel(null, null);
        }
    }

    private static string ModelKey(string path, string skeleton) => $"{path}|{skeleton}";

    // The effect folders the game registers its effect textures from: a model's material names a file, found anywhere
    // under them by name when it is not next to the model.
    private static readonly string[] EffectTextureFolders = ["image/Sinimage/AssaEffect", "Effect/AssaEffect", "Effect/NewEffect/Res"];

    private Dictionary<string, string> EffectTextureIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in EffectTextureFolders)
        {
            var directory = Path.Combine(_assetRoot, folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                index.TryAdd(Path.GetFileName(file), file);
            }
        }

        return index;
    }

    private static PristonModel Relink(PristonModel mesh, IReadOnlyDictionary<string, string> effectTextures)
    {
        var directory = Path.GetDirectoryName(mesh.Path) ?? ".";
        string Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || AssetFiles.Exists(Path.Combine(directory, name))
                || !effectTextures.TryGetValue(Path.GetFileName(name), out var found))
            {
                return name;
            }

            return Path.GetRelativePath(directory, found);
        }

        var materials = mesh.Materials.Select(material => material with
        {
            TextureName = Resolve(material.TextureName),
            OpacityName = Resolve(material.OpacityName)
        }).ToArray();
        return mesh with { Materials = materials };
    }

    public void Dispose()
    {
        foreach (var model in _models.Values)
        {
            model?.Dispose();
        }

        Builtins.Dispose();
    }

    private sealed record ParsedModel(PristonModel? Mesh, PristonModel? Skeleton);

    private sealed record Prepared(VfxCatalog Catalog, ConcurrentDictionary<string, ParsedModel> Models, Dictionary<string, BillboardSequence?> Animations);
}
