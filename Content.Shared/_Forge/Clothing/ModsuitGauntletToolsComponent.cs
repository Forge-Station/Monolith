using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Clothing;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitGauntletToolsComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId UrkProto = "ShipRepairDevice";

    [DataField, AutoNetworkedField]
    public EntProtoId OmnitoolProto = "OmnitoolModsuitGauntlet";

    [DataField, AutoNetworkedField]
    public EntProtoId WelderProto = "WelderExperimental";

    [DataField, AutoNetworkedField]
    public EntProtoId NaniteApplicatorProto = "NaniteApplicatorExperimental";

    [DataField, AutoNetworkedField]
    public EntityUid? UrkEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? OmnitoolEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? WelderEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? NaniteApplicatorEntity;

    [DataField, AutoNetworkedField]
    public bool UrkInHand;

    [DataField, AutoNetworkedField]
    public bool OmnitoolInHand;

    [DataField, AutoNetworkedField]
    public bool WelderInHand;

    [DataField, AutoNetworkedField]
    public bool NaniteApplicatorInHand;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ModsuitGauntletToolComponent : Component
{
    [DataField]
    public EntityUid Gauntlets;

    /// <summary>
    /// Tool is stowed in nullspace; client hides the sprite while this is true.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool StoredHidden;
}
