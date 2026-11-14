using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Vents;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCTrayCrawlerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = false;

    [DataField, AutoNetworkedField]
    public float Range = 4f;
}
