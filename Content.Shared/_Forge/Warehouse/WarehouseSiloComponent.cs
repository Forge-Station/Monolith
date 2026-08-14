using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Warehouse;

/// <summary>
/// Unlimited stack ledger. Nearby stacks on the same grid (default 1 tile) are absorbed as prototype + count.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedWarehouseSiloSystem))]
public sealed partial class WarehouseSiloComponent : Component
{
    /// <summary>
    /// Stack type id → stored count. Not limited.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> Contents = new();

    /// <summary>
    /// How far ground stacks are pulled in. 1.15 covers the silo tile and orthogonal neighbors.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 1.15f;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField, AutoNetworkedField]
    public bool MagnetEnabled = true;
}
