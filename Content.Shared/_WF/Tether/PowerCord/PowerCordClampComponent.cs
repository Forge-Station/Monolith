using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Tether.PowerCord;

/// <summary>
/// The clamp a power cord is bolted to. While two clamps hold the same cord and reference each
/// other, their <c>PowerCordNode</c>s join the two hulls' power nets into one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PowerCordClampComponent : Component
{
    /// <summary>The only rope type that may be tied here. Plain rope and other voltages are refused.</summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<RopeTypePrototype> CordType;

    /// <summary>Power net this clamp taps. Must match the node's own <c>nodeGroupID</c>.</summary>
    [DataField, AutoNetworkedField]
    public NodeGroupID Voltage = NodeGroupID.HVPower;

    /// <summary>Name of the <c>PowerCordNode</c> in this entity's node container.</summary>
    [DataField]
    public string NodeName = "cord";

    /// <summary>The clamp at the far end. Both sides must name each other before either conducts.</summary>
    [AutoNetworkedField]
    public NetEntity? Partner;

    /// <summary>The cord entity joining the two clamps.</summary>
    [AutoNetworkedField]
    public NetEntity? Cord;
}
