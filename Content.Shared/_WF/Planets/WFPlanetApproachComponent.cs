using System.Numerics;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.Planets;

/// <summary>
/// Rides a hull through the hop into or out of orbit so its crew's clients draw the planet approach instead of hyperspace.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, UnsavedComponent]
public sealed partial class WFPlanetApproachComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<StarSystemPrototype> System;

    /// <summary>The body's sector position, which is what picks it out of the system's planets.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 PlanetPosition;

    /// <summary>Where the hull was in the sector when the hop began, or where it will be when it ends.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 ShipPosition;

    /// <summary>True on the way in, false on the way out.</summary>
    [DataField, AutoNetworkedField]
    public bool Arriving;

    /// <summary>The travel leg of the hop, which paces the animation; drawing spans the hull's whole stay on the FTL map.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Start;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan End;

    /// <summary>The hull making the hop, whose arrival clears this mark; server-side only.</summary>
    [ViewVariables]
    public EntityUid Hull;
}
