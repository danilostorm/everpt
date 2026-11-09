using System.Numerics;
using PristonTale.Contracts.Motion;

namespace PristonTale.Client.Net.Presence;

public readonly record struct EntityStateInfo(
    ulong EntityId,
    Vector3 Position,
    float Facing,
    MotionState MoveState,
    int Life,
    int MoveSpeed = 0,
    float AttackRate = 1f,
    ulong AttackTargetId = 0,
    ulong AttackSequence = 0,
    double? ServerTimeMs = null);
