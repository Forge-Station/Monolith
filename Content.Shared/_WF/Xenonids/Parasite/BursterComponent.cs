using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Parasite;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BursterComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid BurstFrom;
}
