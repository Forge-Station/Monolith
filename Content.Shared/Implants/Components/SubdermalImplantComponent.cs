using Content.Shared.Actions;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Implants.Components;

/// <summary>
/// Subdermal implants get stored in a container on an entity and grant the entity special actions
/// The actions can be activated via an action, a passive ability (ie tracking), or a reactive ability (ie on death) or some sort of combination
/// They're added and removed with implanters
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SubdermalImplantComponent : Component
{
    /// <summary>
    /// Used where you want the implant to grant the owner an instant action.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("implantAction")]
    public EntProtoId? ImplantAction;

    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    /// <summary>
    /// The entity this implant is inside
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? ImplantedEntity;

    /// <summary>
    /// Should this implant be removeable?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("permanent"), AutoNetworkedField]
    public bool Permanent = false;

    /// <summary>
    /// Target whitelist for this implant specifically.
    /// Only checked if the implanter allows implanting on the target to begin with.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Target blacklist for this implant specifically.
    /// Only checked if the implanter allows implanting on the target to begin with.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Forge-Change: Components added to the implanted entity while this implant is installed.
    /// Mirrors Shitmed organ/body-part onAdd behavior for YAML-driven implants.
    /// </summary>
    [DataField]
    public ComponentRegistry? OnAdd;

    /// <summary>
    /// Forge-Change: Components added to the implanted entity when this implant is removed.
    /// Components from OnAdd are removed on extraction before these are applied.
    /// </summary>
    [DataField]
    public ComponentRegistry? OnRemove;

    /// <summary>
    /// Forge-Change: Multiplier for heavy carry checks while this implant is installed.
    /// Values above 1 make the wearer count as heavier for carry contests.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CarryAssistMassMultiplier = 1f;

    /// <summary>
    /// Forge-Change: Multiplier applied to carry pickup delay while this implant is installed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CarryAssistPickupDelayModifier = 1f;

    /// <summary>
    /// Forge-Change: Multiplier applied only to carry slowdown penalties.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CarryAssistSlowdownPenaltyModifier = 1f;

    /// <summary>
    /// Forge-Change: Multiplier applied only to pulling slowdown penalties.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PullingAssistSlowdownPenaltyModifier = 1f;

    /// <summary>
    /// Forge-Change: Multiplier for pull-joint reaction applied back to the puller.
    /// Values below 1 reduce the mass penalty from heavy pulled objects without moving them directly.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PullingAssistMassPenaltyModifier = 1f;
    
    /// <summary>
    /// If set, this ProtoId is used when attempting to draw the implant instead.
    /// Useful if the implant is a child to another implant and you don't want to differentiate between them when drawing.
    /// </summary>
    [DataField]
    public EntProtoId? DrawableProtoIdOverride;
}

/// <summary>
/// Used for opening the storage implant via action.
/// </summary>
public sealed partial class OpenStorageImplantEvent : InstantActionEvent
{

}

public sealed partial class UseFreedomImplantEvent : InstantActionEvent
{

}

/// <summary>
/// Used for triggering trigger events on the implant via action
/// </summary>
public sealed partial class ActivateImplantEvent : InstantActionEvent
{

}

/// <summary>
/// Used for opening the uplink implant via action.
/// </summary>
public sealed partial class OpenUplinkImplantEvent : InstantActionEvent
{

}

public sealed partial class UseScramImplantEvent : InstantActionEvent
{

}

public sealed partial class UseDnaScramblerImplantEvent : InstantActionEvent
{

}
