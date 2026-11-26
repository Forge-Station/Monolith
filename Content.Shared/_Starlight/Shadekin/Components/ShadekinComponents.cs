using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Shadekin;

#region Shadekin
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause, AutoGenerateComponentState]
public sealed partial class ShadekinComponent : Component
{
    [DataField]
    public ProtoId<AlertPrototype> ShadekinAlert = "Shadekin";

    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    [DataField]
    public TimeSpan UpdateCooldown = TimeSpan.FromSeconds(1f);

    [AutoNetworkedField, ViewVariables]
    public ShadekinState CurrentState { get; set; } = ShadekinState.Dark;

    [DataField("thresholds", required: true)]
    public SortedDictionary<FixedPoint2, ShadekinState> Thresholds = new();

    [DataField]
    public SoundSpecifier CutoffSound = new SoundPathSpecifier("/Audio/_HL/Effects/ma cutoff.ogg");
}

[Serializable, NetSerializable]
public enum ShadekinState : byte
{
    Invalid = 0,
    Dark = 1,
    Low = 2,
    Annoying = 3,
    High = 4,
    Extreme = 5

}
#endregion

#region Brighteye
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BrighteyeComponent : Component
{
    /// <summary>
    /// Shadekin Portal, if null then the portal does not exist.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Portal;

    [DataField]
    public EntityUid? PortalAction;

    [DataField]
    public EntityUid? CreateShadeAction;

    [DataField]
    public EntProtoId BrighteyePortalAction = "BrighteyePortalAction";

    [DataField]
    public EntProtoId BrighteyeCreateShadeAction = "BrighteyeCreateShadeAction";

    [DataField]
    public EntProtoId ShadekinShadow = "ShadekinShadow";

    [DataField]
    public EntProtoId PortalShadekin = "PortalShadekin";

    [DataField]
    public SoundSpecifier ShadegenSound = new SoundPathSpecifier("/Audio/_Starlight/Effects/Shadekin/nullphase.ogg");
}

#endregion
#region Abilities
public sealed partial class BrighteyePortalActionEvent : InstantActionEvent { }

public sealed partial class BrighteyeCreateShadeActionEvent : InstantActionEvent { }
#endregion
