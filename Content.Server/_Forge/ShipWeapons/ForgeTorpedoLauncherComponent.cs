using Content.Shared.Physics;

namespace Content.Server._Forge.ShipWeapons;

/// <summary>
/// Marks a static ship launcher that uses its own facing for launch direction and
/// the gunnery cursor only for target-grid selection.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeTorpedoLauncherComponent : Component
{
    /// <summary>
    /// Distance from the launcher center used to sample the tile in front of the muzzle.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TileProbeDistance = 1f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ClearanceRayDistance = 1.25f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int ClearanceCollisionMask = (int) (CollisionGroup.Opaque | CollisionGroup.Impassable);

    /// <summary>
    /// Distance used to build a gun shoot coordinate straight out of the launcher.
    /// The actual locked target is stored separately, because the cursor is not the muzzle direction.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LaunchAimDistance = 32f;

    /// <summary>
    /// Minimum random delay before the prepared torpedo launch is actually fired.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LaunchDelayMin = 0f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LaunchDelayMax = 0.1f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TargetSearchRadius = 48f;

    /// <summary>
    /// Minimum tile count for a grid to be considered a valid torpedo target.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MinTargetTiles = 25;
}
