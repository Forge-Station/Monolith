using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.DeployedTraps;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoCaughtInTrapComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan ExpireTime;

    [DataField, AutoNetworkedField]
    public EntityUid? Applier;
}
