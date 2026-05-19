using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class BoardingTeleportExperimentalLootComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId DiskPrototype = "DisciplinesDiskBoardingTeleportExperimental";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DropChance = 0.05f;
}
