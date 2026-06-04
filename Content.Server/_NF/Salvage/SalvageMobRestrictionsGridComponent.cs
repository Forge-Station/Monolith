namespace Content.Server._NF.Salvage;

/// <summary>
///     This component is attached to grids when a salvage mob is
///     spawned on them.
///     This attachment is done by SalvageMobRestrictionsSystem.
///     *Simply put, when this component is removed, the mobs die.*
///     *This applies even if the mobs are off-grid at the time.*
/// </summary>
[RegisterComponent]
public sealed partial class SalvageMobRestrictionsGridComponent : Component
{
    /// <summary>
    /// Forge-Change: Added a list of mobs to kill.
    /// Populated at runtime by <see cref="SalvageMobRestrictionsSystem"/>; must not be map-serialized.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> MobsToKill = new();
}
