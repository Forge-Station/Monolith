using Robust.Shared.Map;

namespace Content.Shared._WF.Xenonids.Construction.Events;

[ByRefEvent]
public record struct XenoSecreteStructureAttemptEvent(EntityCoordinates Target, bool Cancelled = false);
