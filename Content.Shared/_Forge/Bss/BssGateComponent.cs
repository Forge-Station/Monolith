using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Bss;

/// <summary>
/// Identifies a gate grid as an endpoint in a <see cref="BssNetworkPrototype"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BssGateComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<BssNetworkPrototype> Network;

    [DataField(required: true), AutoNetworkedField]
    public string Sector = string.Empty;

    [DataField(required: true), AutoNetworkedField]
    public string Gate = string.Empty;

    [DataField, AutoNetworkedField]
    public float JumpRange = 35f;

    /// <summary>
    /// World-space offset from these gates at which an arriving shuttle is placed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 ArrivalOffset = new(45f, 0f);

    [DataField, AutoNetworkedField]
    public bool Enabled = true;
}
