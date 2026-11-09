using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;

namespace PristonTale.Gameplay.World.Remote;

public interface IModelActorComposer
{
    Task PrepareAsync(string assetRoot, string modelInxPath) => Task.CompletedTask;
    ComposedModelActor Compose(string assetRoot, string modelInxPath, Matrix world, MotionCode motion);
}

// Alternate: the model's sub model on its own skeleton (AlternateModel), hidden until a motion only it has plays.
public readonly record struct ComposedModelActor(ActorHandle Handle, PristonModelInfo Info, ActorHandle Alternate = default,
    PristonModelInfo AlternateInfo = null);

public sealed class ModelActorComposer(IActorScene scene, PristonActorLoader actorLoader) : IModelActorComposer
{
    private readonly IActorScene _scene = scene;
    private readonly PristonActorLoader _actorLoader = actorLoader;
    private readonly Dictionary<string, Task<(PristonActor Main, PristonActor Alternate)>> _prepared = new(StringComparer.OrdinalIgnoreCase);

    public Task PrepareAsync(string assetRoot, string modelInxPath)
    {
        var path = AssetFiles.Combine(assetRoot, modelInxPath);
        if (!_prepared.TryGetValue(path, out var preparation))
        {
            preparation = Task.Run(async () =>
            {
                var actor = _actorLoader.Load(assetRoot, path);
                await _scene.PrepareActorResourcesAsync(actor);
                var alternate = LoadAlternate(assetRoot, actor);
                if (alternate is not null)
                {
                    await _scene.PrepareActorResourcesAsync(alternate);
                }

                return (actor, alternate);
            });
            _prepared.Add(path, preparation);
        }

        return preparation;
    }

    // An unreadable sub model leaves the model without its alternate rather than without the model.
    private PristonActor LoadAlternate(string assetRoot, PristonActor main)
    {
        if (AlternateModel.Path(assetRoot, main.Info) is not { } path)
        {
            return null;
        }

        try
        {
            var alternate = _actorLoader.Load(assetRoot, path);
            return AlternateModel.Alternates(main.Info, alternate.Info) ? alternate : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return null;
        }
    }

    public ComposedModelActor Compose(string assetRoot, string modelInxPath, Matrix world, MotionCode motion)
    {
        var path = AssetFiles.Combine(assetRoot, modelInxPath);
        if (!_prepared.TryGetValue(path, out var preparation) || !preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("model actor must be prepared before composition");
        }

        var (actor, alternate) = preparation.Result;
        var handle = _scene.AddActor(new PristonActorInstance(actor, world, motion));
        if (alternate is null)
        {
            return new ComposedModelActor(handle, actor.Info);
        }

        var alternateHandle = _scene.AddActor(new PristonActorInstance(alternate, world, motion));
        _scene.SetActorHidden(alternateHandle, true);
        return new ComposedModelActor(handle, actor.Info, alternateHandle, alternate.Info);
    }
}
