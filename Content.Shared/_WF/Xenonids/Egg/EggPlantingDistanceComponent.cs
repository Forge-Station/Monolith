using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Egg;

[RegisterComponent, NetworkedComponent]
public sealed partial class EggPlantingDistanceComponent : Component
{
    [DataField]
    public float Distance = 1.5f;
}
