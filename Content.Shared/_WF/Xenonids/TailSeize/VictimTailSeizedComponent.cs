using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.TailSeize;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VictimTailSeizedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan SlowTime = TimeSpan.FromSeconds(0.5);

    [DataField, AutoNetworkedField]
    public TimeSpan RootTime = TimeSpan.FromSeconds(1);
}
