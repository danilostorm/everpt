using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RemoteEntity : RemoteEntityBase
{
    private readonly IActorScene _scene;
    private readonly ActorHandle _body;
    private readonly PlayerActorHandles _handles;

    public RemoteEntity(IActorScene scene, PlayerActorHandles handles, double timeMs, Vector3 position, float facing, MotionState moveState, int life)
        : base(EntityKind.Player, timeMs, position, facing, moveState, life)
    {
        _scene = scene;
        _handles = handles;
        _body = handles.Body;
    }

    public PlayerActorHandles Handles => _handles;

    public override int CopyActorHandles(Span<ActorHandle> destination) => _handles.CopyTo(destination);

    protected override void ApplyWorld(Matrix world)
    {
        _handles.SetWorld(_scene, world);
    }

    protected override void ApplyMotionCommand(MotionCommand command)
    {
        if (command.Kind == MotionCommandKind.Restart)
        {
            _handles.RestartMotion(_scene, command.Motion);
        }
        else
        {
            _handles.SetMotion(_scene, command.Motion);
        }
    }

    protected override void ApplyMotionIndex(int motionIndex)
    {
        // The body's equipment picks the weapon variant of a skill clip; the head follows the same clip.
        var resolved = _scene.ResolveSkillMotion(_body, motionIndex);
        _handles.SetMotionIndex(_scene, resolved);
    }

    public ActorHandle Body => _body;

    // A released charge: the skill clip jumps ahead and plays the rest at the new rate.
    public void SeekSkill(int framesFromStart, float rate)
    {
        _handles.SeekMotion(_scene, framesFromStart);
        ApplyPlaybackRate(rate);
    }

    protected override void ApplyPlaybackRate(float rate)
    {
        _handles.SetPlaybackRate(_scene, rate);
    }

    public override void Dispose()
    {
        _handles.Remove(_scene);
    }
}
