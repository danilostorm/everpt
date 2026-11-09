namespace PristonTale.Simulation.World.Ai;

public readonly record struct MonsterAttackEvent(ulong AttackerEntityId, ulong TargetEntityId, bool IsSkill);
