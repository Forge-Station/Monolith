using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Stun;

[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCSizeStunSystem))]
public sealed partial class RMCKnockOutOnCollideComponent : Component
{
    [DataField]
    public TimeSpan ParalyzeTime;
}
