using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipPa;

/// <summary>Raised on the grid (broadcast) after its situation code changed.</summary>
[ByRefEvent]
public readonly record struct ShipAlertCodeChangedEvent(
    EntityUid Grid,
    ProtoId<ShipAlertCodePrototype> Old,
    ProtoId<ShipAlertCodePrototype> New,
    EntityUid? User);

/// <summary>Raised on the grid (broadcast) when general quarters is sounded or secured.</summary>
[ByRefEvent]
public readonly record struct ShipGeneralQuartersChangedEvent(EntityUid Grid, bool Active, EntityUid? User);
