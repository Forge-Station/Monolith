using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Aura;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedAuraSystem))]
public sealed partial class AuraComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Color;

    [DataField, AutoNetworkedField]
    public TimeSpan? ExpiresAt;

    [DataField, AutoNetworkedField]
    public float OutlineWidth = 2;
}
