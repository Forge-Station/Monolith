using Robust.Shared.Prototypes;

namespace Content.Server._WF.AsteroidMining;

/// <summary>Builds an asteroid grid on map init: invisible tiles in the rock's silhouette with the rock anchored at its centre.</summary>
[RegisterComponent]
public sealed partial class WFAsteroidGridComponent : Component
{
    [DataField(required: true)] public EntProtoId Rock;

    [DataField] public string Tile = "WFAsteroidCore";

    /// <summary>
    /// Grid density over a hull's. Solid rock weighs far more than a ship of the same footprint, which is what lets
    /// collision warnings, autopilot avoidance and impacts treat an asteroid as the obstacle it is.
    /// </summary>
    [DataField] public float DensityScale = 8f;

    public EntityUid? RockUid;
}
