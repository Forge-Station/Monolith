using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]

public sealed partial class XenoConstructionBuildSpeedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float BuildTimeMult = 1;
}
