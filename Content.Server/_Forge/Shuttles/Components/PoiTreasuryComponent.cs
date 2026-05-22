using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.Shuttles.Components;

/// <summary>
/// A captured POI's physical treasury. Anyone can open and view its contents,
/// but only the current capture leader of the host grid can take items out.
/// Items are deposited only by the system: periodic random rewards and sales tax on the same grid.
/// </summary>
[RegisterComponent]
public sealed partial class PoiTreasuryComponent : Component
{
    /// <summary>
    /// Random loot table (<c>entityTable</c> prototypes under Resources/Prototypes).
    /// Preferred over <see cref="RewardPool"/>.
    /// </summary>
    [DataField]
    public EntityTableSelector? RewardTable;

    /// <summary>
    /// Legacy flat pool: one random prototype per tick, spawned <see cref="RewardCount"/> times.
    /// Ignored when <see cref="RewardTable"/> is set.
    /// </summary>
    [DataField]
    public List<EntProtoId> RewardPool = new();

    /// <summary>
    /// Per-treasury override of the reward interval, in minutes.
    /// If null, the global <c>forge.poi_capture.reward_interval_minutes</c> CVar is used.
    /// </summary>
    [DataField]
    public float? RewardIntervalMinutes;

    /// <summary>
    /// How many copies of the rolled prototype to spawn per reward tick.
    /// Useful for stackable currencies; default 1.
    /// </summary>
    [DataField]
    public int RewardCount = 1;

    /// <summary>
    /// Time of the next reward roll. Set on map init to <c>now + interval</c>.
    /// </summary>
    [DataField]
    public TimeSpan NextRewardTime;

    /// <summary>
    /// Coordinates the treasury was spawned at on map init. If the entity is
    /// ever moved away from this position, the system snaps it back.
    /// </summary>
    [DataField]
    public EntityCoordinates? HomePosition;
}
