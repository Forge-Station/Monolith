using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Augments;

/// <summary>The augment picked for a slot, and its style if it is styled.</summary>
[DataRecord, Serializable, NetSerializable]
public partial record struct WFAugmentChoice(ProtoId<WFAugmentPrototype> Augment, ProtoId<WFAugmentStylePrototype>? Style);
