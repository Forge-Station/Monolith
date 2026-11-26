using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics.Components;

/// <summary>
/// Tracks a post-mutation hostility takeover so it can be restored when the gene is properly reverted.
/// </summary>
[RegisterComponent]
public sealed partial class GeneticAggressionComponent : Component
{
    [DataField]
    public string SourceGene = string.Empty;

    [DataField]
    public bool AddedHtn;

    [DataField]
    public string? PreviousTask;

    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> PreviousFactions = new();
}
