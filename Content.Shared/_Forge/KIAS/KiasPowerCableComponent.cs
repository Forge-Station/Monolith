using Content.Shared.Power;

namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasPowerCableComponent : Component
{
    public EntityUid? IndexedGrid;
}

[ByRefEvent]
public readonly record struct KiasPowerDeficitEvent(EntityUid Grid, CableType Channel, float Supply, float Consumption);
