using Robust.Shared.Map;

namespace Content.Server._WF.Planets.Flight;

/// <summary>A hull skidding after a hard landing, shedding leading-edge tiles until friction stops it.</summary>
[RegisterComponent]
public sealed partial class WFSkidComponent : Component
{
    /// <summary>Damage accumulated per hull tile, keyed by grid index.</summary>
    [DataField]
    public Dictionary<Vector2i, float> TileDamage = new();

    /// <summary>Looping scrape sound.</summary>
    [DataField]
    public EntityUid? Loop;

    public EntityUid? ImpactStream;
    public TimeSpan NextImpactSound;
    public TimeSpan NextLoopRefresh;

    /// <summary>When the leading edge is next damaged.</summary>
    [DataField]
    public TimeSpan NextBite;

    /// <summary>When obstacles under the hull are next broken; the wall pass runs every tick.</summary>
    [DataField]
    public TimeSpan NextPlough;

    /// <summary>Ground cells already scarred during this skid.</summary>
    public HashSet<Vector2i> ScarredTiles = new();
    public EntityUid? ScarMap;

    /// <summary>Detached wreckage: wears and scars terrain but skips the occupant crush.</summary>
    public bool Debris;
}
