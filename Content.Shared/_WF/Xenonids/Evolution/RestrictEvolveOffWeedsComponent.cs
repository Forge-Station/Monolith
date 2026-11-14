using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Evolution;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem))]
public sealed partial class RestrictEvolveOffWeedsComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan RestrictTime = TimeSpan.FromSeconds(900);
}
