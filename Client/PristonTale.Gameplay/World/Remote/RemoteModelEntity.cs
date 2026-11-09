using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World.Remote;

// A monster or NPC model. One with an alternate model (AlternateModel) shows it, in the main model's place, while a motion
// only the alternate has plays; Actor is the model shown.
public sealed class RemoteModelEntity : RemoteEntityBase
{
    private readonly IActorScene _scene;
    private readonly ActorHandle _actor;
    private readonly ActorHandle _alternate;
    private readonly PristonModelInfo _info;
    private readonly PristonModelInfo _alternateInfo;
    private readonly float _scale;
    private readonly NpcStandMotionCycle _standMotionCycle;
    private bool _alternateShown;

    public RemoteModelEntity(IActorScene scene, ActorHandle actor, float scale, double timeMs, Vector3 position, float facing, MotionState moveState,
        int life, EntityKind kind = EntityKind.Monster, int soundCode = 0, PristonModelInfo modelInfo = null, ulong entityId = 0,
        ActorHandle alternate = default, PristonModelInfo alternateInfo = null)
        : base(kind, timeMs, position, facing, moveState, life, soundCode)
    {
        _scene = scene;
        _actor = actor;
        _info = modelInfo;
        _alternate = alternate;
        _alternateInfo = alternateInfo;
        _scale = scale <= 0f ? 1f : scale;
        if (kind == EntityKind.Npc && modelInfo is not null)
        {
            _standMotionCycle = new NpcStandMotionCycle(modelInfo, entityId);
        }

        ShowAlternate(MotionCodes.Of(moveState));
    }

    public override void Update(float dt, double nowMs)
    {
        base.Update(dt, nowMs);
        if (_standMotionCycle is null)
        {
            return;
        }

        var update = _standMotionCycle.Update(dt, ShownMotion);
        if (update.MotionChanged)
        {
            _scene.SetActorMotionIndex(_actor, update.MotionIndex);
        }
    }

    protected override void ApplyWorld(Matrix world)
    {
        var scaled = Matrix.CreateScale(_scale) * world;
        _scene.SetActorWorld(_actor, scaled);
        if (_alternate.IsValid)
        {
            _scene.SetActorWorld(_alternate, scaled);
        }
    }

    protected override void ApplyMotionCommand(MotionCommand command)
    {
        ShowAlternate(command.Motion);
        if (command.Kind == MotionCommandKind.Restart)
        {
            _scene.RestartActorMotion(Actor, command.Motion);
        }
        else
        {
            _scene.SetActorMotion(Actor, command.Motion);
        }
    }

    protected override void ApplyPlaybackRate(float rate)
    {
        _scene.SetActorPlaybackRate(_actor, rate);
        if (_alternate.IsValid)
        {
            _scene.SetActorPlaybackRate(_alternate, rate);
        }
    }

    public override int CopyActorHandles(Span<ActorHandle> destination)
    {
        if (!Actor.IsValid || destination.Length == 0)
        {
            return 0;
        }

        destination[0] = Actor;
        return 1;
    }

    // Skill clips are the main model's.
    protected override void ApplyMotionIndex(int motionIndex)
    {
        SetAlternateShown(false);
        _scene.SetActorMotionIndex(_actor, motionIndex);
    }

    public ActorHandle Actor => _alternateShown ? _alternate : _actor;

    private void ShowAlternate(MotionCode motion) => SetAlternateShown(_info is not null && AlternateModel.Shows(_info, _alternateInfo, motion));

    private void SetAlternateShown(bool shown)
    {
        if (!_alternate.IsValid || shown == _alternateShown)
        {
            return;
        }

        _alternateShown = shown;
        _scene.SetActorHidden(_actor, shown);
        _scene.SetActorHidden(_alternate, !shown);
    }

    // A monster's level and model size, which select its effect family.
    public int Level { get; init; }

    public float Scale => _scale;

    public override void Dispose()
    {
        _scene.RemoveActor(_actor);
        if (_alternate.IsValid)
        {
            _scene.RemoveActor(_alternate);
        }
    }
}
