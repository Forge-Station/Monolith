using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets;

/// <summary>Registers a biome encounter site with the bounded planet population system.</summary>
[RegisterComponent]
public sealed partial class WFPlanetFaunaSpawnerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<EntityTablePrototype> Table;

    /// <summary>Living nests remain visible and stop producing encounters when destroyed.</summary>
    [DataField] public bool KeepEntity;
    [DataField] public float SpawnDelay = 180f;
}