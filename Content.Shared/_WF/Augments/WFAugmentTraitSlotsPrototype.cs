using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>
/// The body slots a trait puts its own parts in. A character with the trait can't take augments there, as the trait
/// would replace them when it spawns. The id is the trait's.
/// </summary>
[Prototype("wfAugmentTraitSlots")]
public sealed partial class WFAugmentTraitSlotsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<WFAugmentSlot> Slots = new();
}
