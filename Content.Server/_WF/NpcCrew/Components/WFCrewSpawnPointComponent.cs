using Content.Shared._WF.NpcCrew;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>A map marker that spawns a crewman of a role at map init and is that crewman's post.</summary>
[RegisterComponent]
public sealed partial class WFCrewSpawnPointComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFCrewRolePrototype> Role;

    [DataField]
    public string Group = string.Empty;

    [ViewVariables]
    public EntityUid? Spawned;
}
