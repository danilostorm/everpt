namespace PristonTale.Simulation.Items;

// The three potion boxes on the bottom bar (keys 1 to 3), as the original's: one potion stack each, of at most the
// character's potion space (PlayerCharacter.Belt). Owner views give a belt item the slot FirstOwnerSlot + its box.
public sealed class PotionBelt() : ItemSlots(Boxes)
{
    public const int Boxes = 3;
    // Owner slots follow the equipment's, as the original numbers the belt after it.
    public const int FirstOwnerSlot = Characters.EquipmentSlots.Count;
}
