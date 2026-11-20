using Content.Shared.FixedPoint;

namespace Content.Shared._WF.RMCCore.Stealth;

[ByRefEvent]
public record struct ActiveInvisibleToggledEvent(
    bool enabled
);