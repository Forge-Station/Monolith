using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Links a machine, such as a lathe, to one parts silo and one chemical silo, apart from its material silo.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFabricationSiloSystem))]
public sealed partial class FabricationSiloClientComponent : Component
{
    /// <summary>
    /// Linked parts silo.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? PartsSilo;

    /// <summary>
    /// Linked chemical silo.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ChemicalSilo;

    /// <summary>
    /// Extra parts parts silos accept for this machine, beyond its lathe recipes. Matched on prototype components and
    /// tags only.
    /// </summary>
    [DataField]
    public EntityWhitelist? Parts;
}
