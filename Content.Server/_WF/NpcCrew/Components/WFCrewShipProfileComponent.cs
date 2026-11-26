using Content.Shared._WF.NpcCrew;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>The crew profile a ship was crewed with, on its grid, for the rules a profile sets between its own ships.</summary>
[RegisterComponent]
public sealed partial class WFCrewShipProfileComponent : Component
{
    [DataField]
    public ProtoId<WFCrewProfilePrototype> Profile;
}
