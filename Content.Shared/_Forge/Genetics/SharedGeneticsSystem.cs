using Content.Shared._Forge.Genetics.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Genetics;

public abstract class SharedGeneticsSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager Prototypes = default!;

    public bool TryGetGene(EntityUid uid, string geneId, out GeneState state, GenomeComponent? genome = null)
    {
        state = default!;
        if (!Resolve(uid, ref genome, false))
            return false;

        return genome.Genes.TryGetValue(geneId, out state!);
    }

    public bool HasActiveGene(EntityUid uid, string geneId, GenomeComponent? genome = null)
    {
        return TryGetGene(uid, geneId, out var state, genome) && state.Active;
    }

    public int RecalculateInstability(GenomeComponent genome)
    {
        var total = 0;
        foreach (var (id, state) in genome.Genes)
        {
            if (!state.Active)
                continue;

            if (!Prototypes.TryIndex<GenePrototype>(id, out var proto))
                continue;

            total += proto.Instability;
        }

        genome.Instability = total;
        return total;
    }

    public List<string> GetBranchSequence(GenomeComponent genome, GeneBranch branch)
    {
        if (genome.Branches.TryGetValue(branch, out var existing) && existing.Count >= DnaSequence.BranchLength)
            return existing;

        var created = existing ?? new List<string>(DnaSequence.BranchLength);
        while (created.Count < DnaSequence.BranchLength)
            created.Add("**");
        genome.Branches[branch] = created;
        return created;
    }
}
