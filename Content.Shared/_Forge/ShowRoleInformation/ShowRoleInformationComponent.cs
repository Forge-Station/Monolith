using Robust.Shared.GameStates;

namespace Content.Shared._Forge.ShowRoleInformation;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShowRoleInformationComponent : Component
{
    [DataField("roleName"), AutoNetworkedField]
    public string RoleName;

    [DataField("description"), AutoNetworkedField]
    public string Description;

    [DataField("duration"), AutoNetworkedField]
    public float Duration;
}
