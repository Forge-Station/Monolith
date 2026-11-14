using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Leap;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoLeapSystem))]
public sealed partial class LeapIncapacitatedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan RecoverAt;
}
