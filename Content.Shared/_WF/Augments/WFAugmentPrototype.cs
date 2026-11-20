using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>One choice in the creator's Augments tab for a body slot.</summary>
[Prototype("wfAugment")]
public sealed partial class WFAugmentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public WFAugmentSlot Slot;

    [DataField(required: true)]
    public WFAugmentKind Kind;

    /// <summary>The limb that replaces the slot's part, for <see cref="WFAugmentKind.Limb"/>.</summary>
    [DataField]
    public EntProtoId? Part;

    /// <summary>The organ that replaces the slot's organ, for <see cref="WFAugmentKind.Organ"/>.</summary>
    [DataField]
    public EntProtoId? Organ;

    /// <summary>Whether the player picks a style for it. Limbs and plating are styled.</summary>
    [DataField]
    public bool Styled;

    /// <summary>
    /// One-time price in spesos, taken from the character's bank the first time it is saved with this augment.
    /// Zero for augments that give no advantage.
    /// </summary>
    [DataField]
    public int Price;

    /// <summary>Species that can't take it.</summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> SpeciesBlacklist = new();

    /// <summary>Sort order within the slot, lowest first.</summary>
    [DataField]
    public int Order;
}
