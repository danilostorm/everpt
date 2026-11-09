using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.Items;

namespace PristonTale.Tests;

internal static class ItemRecords
{
    // A regular misc item definition with every authored value at zero; a case sets what it tests with `with`.
    public static ItemTemplateRecord Blank(string code) => new(
        0, code, (short)ItemVariant.Regular, 0, code, (int)ItemKind.Misc, 1, 1, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, 0);
}
