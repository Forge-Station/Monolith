using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.OrePipe;

/// <summary>
/// Expandable abstract ore bank on a shuttle. Capacity grows with <see cref="OreHoldModuleComponent"/>s on the same OrePipe network.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OreHoldComponent : Component
{
    /// <summary>
    /// Base capacity before modules.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int BaseCapacity = 500;

    /// <summary>
    /// Ore entity prototype → unit count stored in the hold.
    /// </summary>
    [DataField, ViewVariables, AutoNetworkedField]
    public Dictionary<EntProtoId, int> Contents = new();

    [ViewVariables]
    public int TotalCount
    {
        get
        {
            var total = 0;
            foreach (var count in Contents.Values)
                total += count;
            return total;
        }
    }
}

/// <summary>
/// Adds capacity to every <see cref="OreHoldComponent"/> on the same OrePipe node group.
/// </summary>
[RegisterComponent]
public sealed partial class OreHoldModuleComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int ExtraCapacity = 500;
}

/// <summary>
/// Marker: when destroyed by a ship drill, spawned ore is absorbed into the drill buffer.
/// </summary>
[RegisterComponent]
public sealed partial class OreDrillHarvestTargetComponent : Component
{
    /// <summary>
    /// Last ship drill that damaged this mob (used to route destructible ore drops).
    /// </summary>
    [ViewVariables]
    public EntityUid? LastDrill;
}
