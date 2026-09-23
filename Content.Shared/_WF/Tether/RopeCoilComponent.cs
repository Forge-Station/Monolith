using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Tether;

/// <summary>A stack of rope. Used on one attach point, then on a second, to tie a rope between them.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RopeCoilComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<RopeTypePrototype> RopeType;

    /// <summary>Metres of rope per stack unit.</summary>
    [DataField, AutoNetworkedField] public float MetresPerUnit = 1f;

    /// <summary>Rope laid per metre of separation, so a tied rope starts slack.</summary>
    [DataField, AutoNetworkedField] public float Slack = 1.15f;
}
