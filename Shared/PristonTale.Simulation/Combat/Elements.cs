namespace PristonTale.Simulation.Combat;

public enum Element
{
    Bionic = 0,
    Earth = 1,
    Fire = 2,
    Ice = 3,
    Lighting = 4,
    Poison = 5,
    Water = 6,
    Wind = 7
}

public readonly record struct ResistanceBlock(
    short Bionic,
    short Earth,
    short Fire,
    short Ice,
    short Lighting,
    short Poison,
    short Water,
    short Wind)
{
    public static readonly ResistanceBlock Zero = default;

    public short this[Element e] => e switch
    {
        Element.Bionic => Bionic,
        Element.Earth => Earth,
        Element.Fire => Fire,
        Element.Ice => Ice,
        Element.Lighting => Lighting,
        Element.Poison => Poison,
        Element.Water => Water,
        Element.Wind => Wind,
        _ => 0
    };

    public ResistanceBlock Add(in ResistanceBlock o) => new(
        (short)(Bionic + o.Bionic),
        (short)(Earth + o.Earth),
        (short)(Fire + o.Fire),
        (short)(Ice + o.Ice),
        (short)(Lighting + o.Lighting),
        (short)(Poison + o.Poison),
        (short)(Water + o.Water),
        (short)(Wind + o.Wind));
}
