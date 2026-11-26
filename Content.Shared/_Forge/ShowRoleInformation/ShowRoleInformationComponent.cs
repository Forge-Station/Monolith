using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.ShowRoleInformation;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShowRoleInformationComponent : Component
{
    [DataField("window", required: true), AutoNetworkedField]
    public ProtoId<ShowRoleInformationWindowData> Window;

    [DataField("skipWindows"), AutoNetworkedField]
    public HashSet<ProtoId<ShowRoleInformationWindowData>> SkipWindows = [];

    [DataField("duration"), AutoNetworkedField]
    public float Duration = 15;
}
