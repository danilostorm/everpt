namespace PristonTale.Simulation.Items;

// An item set: once Pieces of its items are worn, the wearer gains the bonus (max life, absorption, block and the
// regeneration of the three pools).
public sealed record ItemSetDefinition(int SetCode, int Pieces, int Life, float Absorb, float Block, float LifeRegen, float ManaRegen,
    float StaminaRegen);
