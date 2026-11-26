using Content.Shared._WF.Xenonids.Fruit.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Fruit.Events;

// Raised on the fruit planter to update their visuals (if applicable)
[ByRefEvent]
public readonly record struct XenoFruitPlanterVisualsChangedEvent(EntProtoId<XenoFruitComponent> Choice);
