using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Hive.Components;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveDevouredComponent : Component
{

    [DataField, AutoNetworkedField]
    public EntityUid? DevouredBy;
}
