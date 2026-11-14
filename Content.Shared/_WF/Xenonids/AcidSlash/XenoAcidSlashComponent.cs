using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.AcidSlash;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoAcidSlashSystem))]
public sealed partial class XenoAcidSlashComponent : Component
{
    [DataField]
    public ComponentRegistry? Acid;
}
