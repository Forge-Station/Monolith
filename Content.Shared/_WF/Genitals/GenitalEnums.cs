using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Genitals;

/// <summary>Organ slot a genital organ occupies.</summary>
[Serializable, NetSerializable]
public enum GenitalSlot : byte
{
    Penis,
    Testicles,
    Vagina,
    Womb,
    Breasts,
}

/// <summary>Body regions clothing and undergarments can cover.</summary>
[Flags, Serializable, NetSerializable]
public enum GenitalRegion : byte
{
    None = 0,
    Chest = 1 << 0,
    Groin = 1 << 1,
    All = Chest | Groin,
}

[Serializable, NetSerializable]
public enum SheathType : byte
{
    None,
    Sheath,
    Slit,
}

/// <summary>Profile-level choice. None means no testicle organ is built.</summary>
[Serializable, NetSerializable]
public enum TesticleType : byte
{
    None,
    External,
    Internal,
}

[Serializable, NetSerializable]
public enum GenitalVisibility : byte
{
    Normal,
    AlwaysHidden,
    ShowThroughClothing,
}

[Serializable, NetSerializable]
public enum GenitalRevealMode : byte
{
    UndergarmentRemoval,
    ClothingRemoval,
}

[Serializable, NetSerializable]
public enum ArousalState : byte
{
    None,
    Partial,
    Full,
}

/// <summary>Runtime undergarment state. ByOther bits mark removals by another player; they are undone when the owner revokes UndergarmentStrip.</summary>
[Flags, Serializable, NetSerializable]
public enum UndergarmentFlags : byte
{
    None = 0,
    TopRemoved = 1 << 0,
    BottomRemoved = 1 << 1,
    TopByOther = 1 << 2,
    BottomByOther = 1 << 3,
}

[Serializable, NetSerializable]
public enum UndergarmentSlot : byte
{
    Top,
    Bottom,
}

/// <summary>What an undergarment slot maps to: its marking category, body region and runtime flags.</summary>
public static class UndergarmentSlots
{
    public static MarkingCategories CategoryOf(UndergarmentSlot slot)
    {
        return slot == UndergarmentSlot.Top ? MarkingCategories.UndergarmentTop : MarkingCategories.UndergarmentBottom;
    }

    /// <summary>The slot of an undergarment marking category; null for any other category.</summary>
    public static UndergarmentSlot? SlotOf(MarkingCategories category)
    {
        return category switch
        {
            MarkingCategories.UndergarmentTop => UndergarmentSlot.Top,
            MarkingCategories.UndergarmentBottom => UndergarmentSlot.Bottom,
            _ => null,
        };
    }

    public static GenitalRegion RegionOf(UndergarmentSlot slot)
    {
        return slot == UndergarmentSlot.Top ? GenitalRegion.Chest : GenitalRegion.Groin;
    }

    /// <summary>The slot's undergarment is removed.</summary>
    public static UndergarmentFlags RemovedFlag(UndergarmentSlot slot)
    {
        return slot == UndergarmentSlot.Top ? UndergarmentFlags.TopRemoved : UndergarmentFlags.BottomRemoved;
    }

    /// <summary>Another player removed the slot's undergarment.</summary>
    public static UndergarmentFlags ByOtherFlag(UndergarmentSlot slot)
    {
        return slot == UndergarmentSlot.Top ? UndergarmentFlags.TopByOther : UndergarmentFlags.BottomByOther;
    }

    /// <summary>The Removed and ByOther bits of one slot.</summary>
    public static UndergarmentFlags SlotBits(UndergarmentSlot slot)
    {
        return RemovedFlag(slot) | ByOtherFlag(slot);
    }
}

/// <summary>Which anchor a slot draws against this frame.</summary>
[Serializable, NetSerializable]
public enum GenitalLayerSet : byte
{
    Hidden,
    Under,
    Over,
}

/// <summary>Creator preview override. Client-only.</summary>
[Serializable, NetSerializable]
public enum GenitalPreviewMode : byte
{
    AsWorn,
    UnderwearOnly,
    Nude,
}
