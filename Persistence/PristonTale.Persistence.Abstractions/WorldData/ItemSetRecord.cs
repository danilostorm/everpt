namespace PristonTale.Persistence.Abstractions.WorldData;

// The bonus an item set gives once Pieces of its items are worn.
public sealed record ItemSetRecord(
    int SetCode,
    int Pieces,
    int Life,
    float Absorb,
    float Block,
    float LifeRegen,
    float ManaRegen,
    float StaminaRegen);
