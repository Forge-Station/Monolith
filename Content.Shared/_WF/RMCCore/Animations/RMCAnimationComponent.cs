using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Animations;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCAnimationSystem))]
public sealed partial class RMCAnimationComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<RMCAnimationId, RMCAnimation> Animations = new();
}
