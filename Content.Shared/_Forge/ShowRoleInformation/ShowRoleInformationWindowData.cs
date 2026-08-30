using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.ShowRoleInformation;

[Prototype("showRoleInformationWindow")]
public sealed partial class ShowRoleInformationWindowData : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("roleName", required: true)]
    public string RoleName = string.Empty;

    [DataField("description", required: true)]
    public string Description = string.Empty;
}
