using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Evolution;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem))]
public sealed partial class XenoRecentlyDevolvedComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId, TimeSpan> Recent = new();
}
