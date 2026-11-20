using Content.Shared._WF.Xenobiology;
using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Natives;

/// <summary>
/// On a world's layers: the life born there needs no air. Its natives are what its wildlife tables spawn, the listed
/// entities and slimes of the listed breeds; each gets <c>BreathingImmunity</c> on that world.
/// </summary>
[RegisterComponent]
public sealed partial class WFPlanetNativesComponent : Component
{
    [DataField]
    public List<ProtoId<EntityTablePrototype>> Tables = new();

    [DataField]
    public List<EntProtoId> Entities = new();

    [DataField]
    public List<ProtoId<BreedPrototype>> Breeds = new();

    /// <summary>Every prototype the tables and list name, resolved on first use.</summary>
    [ViewVariables]
    public HashSet<string>? Resolved;
}
