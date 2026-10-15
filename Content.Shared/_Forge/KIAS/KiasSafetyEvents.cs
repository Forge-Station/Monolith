using Content.Shared.Atmos.Monitor;

namespace Content.Shared._Forge.KIAS;

[ByRefEvent]
public readonly record struct KiasAnomalyGrowthEvent(EntityUid Grid, EntityUid Source, string Location);

[ByRefEvent]
public readonly record struct KiasAtmosStateChangedEvent(EntityUid Grid, EntityUid Source, AtmosAlarmType State, string Location);
