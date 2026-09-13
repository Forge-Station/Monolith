using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Genitals;

/// <summary>DoAfter for another player removing or putting back an undergarment.</summary>
[Serializable, NetSerializable]
public sealed partial class UndergarmentStripDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public UndergarmentSlot Slot;

    /// <summary>False means put back.</summary>
    [DataField]
    public bool Remove;

    public override DoAfterEvent Clone()
    {
        return new UndergarmentStripDoAfterEvent { Slot = Slot, Remove = Remove };
    }
}
