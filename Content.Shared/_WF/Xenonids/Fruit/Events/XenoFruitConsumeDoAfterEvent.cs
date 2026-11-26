using Content.Shared._WF.Xenonids.Fruit.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Fruit.Events;

[Serializable, NetSerializable]
public sealed partial class XenoFruitConsumeDoAfterEvent : DoAfterEvent

{
    public override DoAfterEvent Clone()
    {
        return this;
    }
}
