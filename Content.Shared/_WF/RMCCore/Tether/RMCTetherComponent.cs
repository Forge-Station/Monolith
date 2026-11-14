using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Shared._WF.RMCCore.Tether;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCTetherComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool VisibleToOrigin = true;

    [DataField, AutoNetworkedField]
    public MapCoordinates? StaticTetherOrigin;

    [DataField, AutoNetworkedField]
    public EntityUid? TetherOrigin;

    [DataField, AutoNetworkedField]
    public ResPath RsiPath = new("/Textures/_WF/RMCCore/Effects/beam.rsi");

    [DataField, AutoNetworkedField]
    public string TetherState = "oppressor_tail";

    [DataField, AutoNetworkedField]
    public float TetherWidth = 0.5f;

    [DataField, AutoNetworkedField]
    public TimeSpan? RemoveAt;
}
