using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.CriticalGrace;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CriticalGraceTimeComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan GraceDuration = TimeSpan.FromSeconds(1);
}
