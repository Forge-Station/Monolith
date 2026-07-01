using Content.Shared.Physics;

namespace Content.Server._Forge.ShipWeapons;

/// <summary>
/// Marks a ship weapon as a torpedo launcher that only needs a clear muzzle lane
/// instead of full line of sight to the gunnery cursor.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeTorpedoLauncherComponent : Component
{
    /// <summary>
    /// Distance from the launcher center used to sample the tile in front of the muzzle.
    /// That tile must be empty/space, because torpedoes prefer vacuum over chewing the hull.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TileProbeDistance = 1f;

    /// <summary>
    /// Short physics ray distance used to catch hard blockers immediately in front of the muzzle.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ClearanceRayDistance = 1.25f;

    /// <summary>
    /// Collision mask for the short muzzle clearance ray.
    /// </summary>
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

    /// <summary>
    /// Maximum random delay before the prepared torpedo launch is actually fired.
    /// This desynchronizes grouped tubes so the payloads do not all elbow each other at once.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LaunchDelayMax = 0.1f;

    /// <summary>
    /// How far from the gunnery cursor the launcher may look for a grid to lock.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TargetSearchRadius = 48f;

    /// <summary>
    /// Minimum amount of tiles a grid needs to be considered a torpedo target.
    /// This keeps expensive fireworks from chasing loose scrap, because why would it.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MinTargetTiles = 25;
}
