namespace PristonTale.Simulation.Items;

// The glow a unique weapon's file gives it: signed colour offsets (0..255 a full channel; alpha below 0 fades the item
// as it pulses, above 0 keeps it translucent), the pulse (each way lasts 2^Blink ms; below 0 a steady colour) and the
// scale of the item at the pulse's peak (0 none).
public readonly record struct ItemGlow(short R, short G, short B, short A, int Blink, float Scale)
{
    public static readonly ItemGlow None = default;

    public bool Visible => Blink != 0 && (R != 0 || G != 0 || B != 0 || A != 0);
}
