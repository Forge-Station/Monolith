using Content.Shared.Chemistry.Reagent;

namespace Content.Shared._HL.Railroading.Events;

[ByRefEvent]
public record struct RailroadingReagentMetabolizedEvent(ReagentQuantity Reagent);
