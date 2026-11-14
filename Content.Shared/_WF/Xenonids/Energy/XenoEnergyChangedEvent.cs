using Content.Shared.FixedPoint;

namespace Content.Shared._WF.Xenonids.Energy;

[ByRefEvent]
public record struct XenoEnergyChangedEvent(FixedPoint2 NewEnergy);
