using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ManageHive;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ManageHiveSystem))]
public sealed partial class ManageHiveComponent : Component
{
    [DataField, AutoNetworkedField]
    public FixedPoint2 DevolvePlasmaCost = 500;

    [DataField, AutoNetworkedField]
    public FixedPoint2 SacrificeBurrowedLarvaForEvolutionCost = 100;
}
