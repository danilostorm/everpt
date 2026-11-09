using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// The values of one owned item copy that the owner's tooltips and equipment rules need: durability, rolled stats,
// requirements, price, class specialization, the fixed values of its kind, the aging under way and the mix's highest
// sheltom. Flags: bit 0 two-handed, bit 1 the owner's class may use the item.
public sealed record ItemDetailEntry : IWireEntry<ItemDetailEntry>
{
    public const int Size = 16 + 4 * 37 + 4 * 14 + 2 * 8;

    static int IWireEntry<ItemDetailEntry>.Size => Size;
    public const int FlagTwoHanded = 1;
    public const int FlagClassAllowed = 2;

    public Guid InstanceId { get; init; }
    public int DurabilityCurrent { get; init; }
    public int DurabilityMax { get; init; }
    public int UpgradeLevel { get; init; }
    public int DamageMin { get; init; }
    public int DamageMax { get; init; }
    public int AttackRating { get; init; }
    public int Defence { get; init; }
    public int AddLife { get; init; }
    public int AddMana { get; init; }
    public int AddStamina { get; init; }
    public int RequiredLevel { get; init; }
    public int RequiredStrength { get; init; }
    public int RequiredSpirit { get; init; }
    public int RequiredTalent { get; init; }
    public int RequiredAgility { get; init; }
    public int RequiredHealth { get; init; }
    public int Price { get; init; }
    public int Critical { get; init; }
    public int AttackSpeed { get; init; }
    public int ShootingRange { get; init; }
    public int PotionSpace { get; init; }
    public int Weight { get; init; }
    public int UniqueItem { get; init; }
    public int Flags { get; init; }
    public int AgingCount { get; init; }
    public int AgingGoal { get; init; }
    public int MixSheltom { get; init; }
    public int MixStats { get; init; }
    public uint SpecJob { get; init; }
    public int SpecDefence { get; init; }
    public int SpecAttackSpeed { get; init; }
    public int SpecCritical { get; init; }
    public int SpecShootingRange { get; init; }
    public int SpecLevelLife { get; init; }
    public int SpecLevelMana { get; init; }
    public int SpecLevelAttackRating { get; init; }
    public int SpecLevelDamage { get; init; }
    public float Absorb { get; init; }
    public float Block { get; init; }
    public float MoveSpeed { get; init; }
    public float LifeRegen { get; init; }
    public float ManaRegen { get; init; }
    public float StaminaRegen { get; init; }
    public float MagicMastery { get; init; }
    public float SpecAbsorb { get; init; }
    public float SpecMoveSpeed { get; init; }
    public float SpecBlock { get; init; }
    public float SpecMagicMastery { get; init; }
    public float SpecLifeRegen { get; init; }
    public float SpecManaRegen { get; init; }
    public float SpecStaminaRegen { get; init; }

    // Bionic, earth, fire, ice, lightning, poison, water, wind.
    public short[] Resistances { get; init; } = new short[8];

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(InstanceId);
        foreach (var value in (ReadOnlySpan<int>)[DurabilityCurrent, DurabilityMax, UpgradeLevel, DamageMin, DamageMax, AttackRating, Defence,
            AddLife, AddMana, AddStamina, RequiredLevel, RequiredStrength, RequiredSpirit, RequiredTalent, RequiredAgility, RequiredHealth, Price,
            Critical, AttackSpeed, ShootingRange, PotionSpace, Weight, UniqueItem, Flags, AgingCount, AgingGoal, MixSheltom, MixStats])
        {
            writer.Int32(value);
        }

        writer.UInt32(SpecJob);
        foreach (var value in (ReadOnlySpan<int>)[SpecDefence, SpecAttackSpeed, SpecCritical, SpecShootingRange, SpecLevelLife, SpecLevelMana,
            SpecLevelAttackRating, SpecLevelDamage])
        {
            writer.Int32(value);
        }

        foreach (var value in (ReadOnlySpan<float>)[Absorb, Block, MoveSpeed, LifeRegen, ManaRegen, StaminaRegen, MagicMastery, SpecAbsorb,
            SpecMoveSpeed, SpecBlock, SpecMagicMastery, SpecLifeRegen, SpecManaRegen, SpecStaminaRegen])
        {
            writer.Single(value);
        }

        for (var element = 0; element < 8; element++)
        {
            writer.Int16(element < Resistances.Length ? Resistances[element] : (short)0);
        }
    }

    public static ItemDetailEntry Read(ref PacketReader r)
    {
        var id = r.Guid();
        var ints = new int[28];
        for (var index = 0; index < ints.Length; index++)
        {
            ints[index] = r.Int32();
        }

        var specJob = r.UInt32();
        var spec = new int[8];
        for (var index = 0; index < spec.Length; index++)
        {
            spec[index] = r.Int32();
        }

        var floats = new float[14];
        for (var index = 0; index < floats.Length; index++)
        {
            floats[index] = r.Single();
        }

        var resistances = new short[8];
        for (var element = 0; element < resistances.Length; element++)
        {
            resistances[element] = r.Int16();
        }

        return new ItemDetailEntry
        {
            InstanceId = id,
            DurabilityCurrent = ints[0], DurabilityMax = ints[1], UpgradeLevel = ints[2], DamageMin = ints[3], DamageMax = ints[4],
            AttackRating = ints[5], Defence = ints[6], AddLife = ints[7], AddMana = ints[8], AddStamina = ints[9], RequiredLevel = ints[10],
            RequiredStrength = ints[11], RequiredSpirit = ints[12], RequiredTalent = ints[13], RequiredAgility = ints[14], RequiredHealth = ints[15],
            Price = ints[16], Critical = ints[17], AttackSpeed = ints[18], ShootingRange = ints[19], PotionSpace = ints[20], Weight = ints[21],
            UniqueItem = ints[22], Flags = ints[23], AgingCount = ints[24], AgingGoal = ints[25], MixSheltom = ints[26], MixStats = ints[27],
            SpecJob = specJob,
            SpecDefence = spec[0], SpecAttackSpeed = spec[1], SpecCritical = spec[2], SpecShootingRange = spec[3], SpecLevelLife = spec[4],
            SpecLevelMana = spec[5], SpecLevelAttackRating = spec[6], SpecLevelDamage = spec[7],
            Absorb = floats[0], Block = floats[1], MoveSpeed = floats[2], LifeRegen = floats[3], ManaRegen = floats[4], StaminaRegen = floats[5],
            MagicMastery = floats[6], SpecAbsorb = floats[7], SpecMoveSpeed = floats[8], SpecBlock = floats[9], SpecMagicMastery = floats[10],
            SpecLifeRegen = floats[11], SpecManaRegen = floats[12], SpecStaminaRegen = floats[13],
            Resistances = resistances
        };
    }
}

// {size, code, Version, Flags, Revision, Part, Parts, Count, entries}. Sent before the inventory parts of the same revision.
public sealed record ItemDetailsPartPacket(long Revision, int Part, int Parts, IReadOnlyList<ItemDetailEntry> Items)
{
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 8 + 4 + 8 + 4 * 3;
    public const int MaxItemsPerPart = 24;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemDetails);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Int64(Revision);
        writer.Int32(Part);
        writer.Int32(Parts);
        WireList.Write(ref writer, Items);

        return writer.Finish();
    }

    public static ItemDetailsPartPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        if (reader.UInt16() != CurrentVersion)
        {
            throw new InvalidDataException("Item details have an unknown version.");
        }

        reader.UInt16();
        var revision = reader.Int64();
        var part = reader.Int32();
        var parts = reader.Int32();
        if (parts < 1 || part < 0 || part >= parts)
        {
            throw new InvalidDataException("Item details part has an invalid part number.");
        }

        return new ItemDetailsPartPacket(revision, part, parts, WireList.Read<ItemDetailEntry>(ref reader, MaxItemsPerPart, "Item details part"));
    }
}

// What the owner's worn items add beyond the owner status: carried and maximum weight, critical and block chance,
// shooting range, regeneration, belt capacity, magic mastery and the eight element resistances.
public sealed record OwnerItemStatusPacket(
    int CarriedWeight,
    int MaxWeight,
    int CriticalChance,
    int BlockChance,
    int ShootingRange,
    int AttackSpeed,
    int PotionSpace,
    float LifeRegen,
    float ManaRegen,
    float StaminaRegen,
    float MagicMastery,
    short[] Resistances)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 * 7 + 4 * 4 + 2 * 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.OwnerItemStatus);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Int32(CarriedWeight);
        writer.Int32(MaxWeight);
        writer.Int32(CriticalChance);
        writer.Int32(BlockChance);
        writer.Int32(ShootingRange);
        writer.Int32(AttackSpeed);
        writer.Int32(PotionSpace);
        writer.Single(LifeRegen);
        writer.Single(ManaRegen);
        writer.Single(StaminaRegen);
        writer.Single(MagicMastery);
        for (var element = 0; element < 8; element++)
        {
            writer.Int16(element < Resistances.Length ? Resistances[element] : (short)0);
        }

        return writer.Finish();
    }

    public static OwnerItemStatusPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        if (packet.Length != Size || r.UInt16() != CurrentVersion)
        {
            throw new InvalidDataException("Owner item status has an invalid size or version.");
        }

        r.UInt16();

        var values = new int[7];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = r.Int32();
        }

        var floats = new float[4];
        for (var index = 0; index < floats.Length; index++)
        {
            floats[index] = r.Single();
        }

        var resistances = new short[8];
        for (var element = 0; element < resistances.Length; element++)
        {
            resistances[element] = r.Int16();
        }

        return new OwnerItemStatusPacket(values[0], values[1], values[2], values[3], values[4], values[5], values[6], floats[0], floats[1], floats[2],
            floats[3], resistances);
    }
}

public readonly record struct AgingProgressEntry(Guid InstanceId, int Count, int Goal);

// The counts of the owner's worn aging copies toward their goals: {uint16 version, uint16 count, entries {Guid, int32
// Count, int32 Goal}}. Sent when a count moves; the item details carry the same values at each inventory revision.
public sealed record AgingProgressPacket(AgingProgressEntry[] Entries)
{
    public const ushort CurrentVersion = 1;
    public const int MaxEntries = 16;
    public const int EntrySize = 16 + 4 + 4;
    public const int HeaderSize = 8 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.AgingProgress);
        var count = Math.Min(Entries.Length, MaxEntries);
        writer.UInt16(CurrentVersion);
        writer.UInt16((ushort)count);
        for (var index = 0; index < count; index++)
        {
            writer.Guid(Entries[index].InstanceId);
            writer.Int32(Entries[index].Count);
            writer.Int32(Entries[index].Goal);
        }

        return writer.Finish();
    }

    public static AgingProgressPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, HeaderSize);
        if (r.UInt16() != CurrentVersion)
        {
            throw new InvalidDataException("Aging progress has an invalid version.");
        }

        var count = r.UInt16();
        if (count > MaxEntries || packet.Length != HeaderSize + count * EntrySize)
        {
            throw new InvalidDataException("Aging progress has an invalid size.");
        }

        var entries = new AgingProgressEntry[count];
        for (var index = 0; index < count; index++)
        {
            entries[index] = new AgingProgressEntry(r.Guid(), r.Int32(), r.Int32());
        }

        return new AgingProgressPacket(entries);
    }
}
