using System.Numerics;
using Content.Shared.Weapons.Hitscan.Events;

namespace Content.Shared._WF.ShipShields;

/// <summary>Allows shields to clip a resolved hitscan before visuals and damage.</summary>
[ByRefEvent]
public record struct WFShipShieldHitscanTraceEvent(EntityUid Hitscan, HitscanRaycastFiredEvent Trace)
{
    /// <summary>Retains piercing targets before the shield contact.</summary>
    public Dictionary<EntityUid, float>? HitDistances;
    /// <summary>Checks diffraction ordering without charging the field twice.</summary>
    public bool ProbeOnly;
}

/// <summary>Reports an intercepted beam at its shield-local contact point.</summary>
[ByRefEvent]
public record struct WFShipShieldHitscanImpactEvent(EntityUid Hitscan, Vector2 Position, float Strength,
    EntityUid? Gun = null, EntityUid? Shooter = null);
