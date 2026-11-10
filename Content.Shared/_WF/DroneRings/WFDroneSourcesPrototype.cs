using Robust.Shared.Prototypes;

namespace Content.Shared._WF.DroneRings;

/// <summary>Debris-table entries that spawn drones. A space biome that lists one counts as drone space.</summary>
[Prototype("wfDroneSources")]
public sealed partial class WFDroneSourcesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<EntProtoId> Spawners = new();
}
