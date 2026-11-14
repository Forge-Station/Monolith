using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Evolution;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEvolutionSystem), typeof(SharedXenoHiveSystem))]
public sealed partial class EvolutionOverrideComponent : Component
{
    [DataField, AutoNetworkedField]
    public FixedPoint2 Amount;
}
