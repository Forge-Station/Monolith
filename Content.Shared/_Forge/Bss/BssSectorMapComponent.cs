using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Bss;

/// <summary>
/// Marks a map entity as a runtime sector in a <see cref="BssNetworkPrototype"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BssSectorMapComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<BssNetworkPrototype> Network;

    [DataField, AutoNetworkedField]
    public string Sector = string.Empty;

    [DataField, AutoNetworkedField]
    public bool Primary;
}
