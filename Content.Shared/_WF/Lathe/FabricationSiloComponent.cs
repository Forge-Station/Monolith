using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Stores lathe recipe parts or reagents for linked lathes.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFabricationSiloSystem))]
public sealed partial class FabricationSiloComponent : Component
{
    /// <summary>
    /// Container that holds a parts silo's stock.
    /// </summary>
    public const string PartsContainerId = "fabrication_parts";

    /// <summary>
    /// Item slot a chemical silo withdraws reagents into.
    /// </summary>
    public const string ContainerSlotId = "beakerSlot";

    /// <summary>
    /// Which kind of supply this silo holds.
    /// </summary>
    [DataField]
    public FabricationSiloKind Kind;

    /// <summary>
    /// Furthest a linked machine can be.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 125f;

    /// <summary>
    /// Most entities a parts silo holds.
    /// </summary>
    [DataField]
    public int MaxParts = 250;

    /// <summary>
    /// Solution a chemical silo stores its reagents in.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    /// Machines linked to this silo.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> Clients = new();

    /// <summary>
    /// Stored parts; only a parts silo has one.
    /// </summary>
    [ViewVariables]
    public Container? Parts;
}

/// <summary>
/// The supply a fabrication silo holds.
/// </summary>
[Serializable, NetSerializable]
public enum FabricationSiloKind : byte
{
    Parts,
    Chemicals,
}
