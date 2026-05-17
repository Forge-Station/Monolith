using Robust.Shared.GameStates;
using System;

namespace Content.Shared._Mono.Detection;

/// <summary>
///     Component that allows an entity to store heat for infrared signature detection logic.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ThermalSignatureComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public float LastUpdateHeat = 0f;

    [DataField]
    public float StoredHeat = 0f;

    /// <summary>
    ///     Stored heat retains this portion of itself every next second.
    /// </summary>
    [DataField]
    public float HeatDissipation = 15f / 16f;

    /// <summary>
    ///     For grids, the combined stored heat of all entities on the grid.
    ///     For other entities, their stored heat.
    ///     Don't attempt to modify.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float TotalHeat = 0f;

    // Forge-Change-Start: server-only emission cache + active-source bookkeeping.
    /// <summary>
    /// Cached emission (heat per second) sourced from <see cref="GetThermalSignatureEvent"/>.
    /// Recomputed lazily when <see cref="EmissionDirty"/> is set; events such as power toggles
    /// and FTL state changes flip this flag instead of recomputing every tick.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float CachedEmission = 0f;

    /// <summary>
    /// When true, the next thermal update will recompute <see cref="CachedEmission"/> via the directed event.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool EmissionDirty = true;

    /// <summary>
    /// Number of consecutive update ticks during which this source had no meaningful emission and
    /// effectively no stored heat. Once it crosses <c>IdleTicksToPrune</c>, the system stops tracking it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public byte IdleTicks = 0;

    /// <summary>
    /// Server time at which this grid's <see cref="TotalHeat"/> was last replicated. Used to enforce a
    /// minimum interval between Dirty calls per grid, on top of the relative/absolute change thresholds.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan LastDirtyTime;
    // Forge-Change-End
}
