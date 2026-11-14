namespace Content.Server._WF.ShipShields;

/// <summary>A protected grid absorbed a damaging shot attributed to another grid.</summary>
[ByRefEvent]
public readonly record struct WFShipShieldAttackedEvent(EntityUid Grid, EntityUid AttackerGrid, EntityUid? Shooter,
    EntityUid? Weapon = null);
