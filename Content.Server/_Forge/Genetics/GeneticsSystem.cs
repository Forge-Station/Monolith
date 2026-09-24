using System.Linq;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.Cloning;
using Content.Shared.Damage;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem : SharedGeneticsSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private const string SteelDamageContainer = "Silicon";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GenomeComponent, CloningEvent>(OnCloned);
        SubscribeLocalEvent<GenomeComponent, MapInitEvent>(OnGenomeInit);
        InitializeRadiation();
        InitializeInstability();
        InitializeRecipes();
        InitializeMorph();
        InitializeExamine();
    }

    private void OnGenomeInit(Entity<GenomeComponent> ent, ref MapInitEvent args)
    {
        EnsureRecipes();
        EnsureBranches(ent.Comp);
        Dirty(ent);
    }

    public void EnsureBranches(GenomeComponent genome)
    {
        foreach (GeneBranch branch in Enum.GetValues<GeneBranch>())
        {
            if (!genome.Branches.TryGetValue(branch, out var existing) || existing.Count == 0)
            {
                genome.Branches[branch] = DnaSequence.Random(_random, DnaSequence.BranchLength);
                continue;
            }

            while (existing.Count < DnaSequence.BranchLength)
                existing.Add(_random.Pick(DnaSequence.Codons));
        }
    }

    private void OnCloned(Entity<GenomeComponent> ent, ref CloningEvent args)
    {
        // Genome is copied via ITransferredByCloning after MapInit; re-apply active gene components on the clone.
        if (!TryComp<GenomeComponent>(args.Target, out var cloneGenome))
            return;

        foreach (var (id, state) in cloneGenome.Genes)
        {
            if (!state.Active)
                continue;

            state.AppliedComponents.Clear();
            ApplyGeneComponents(args.Target, id, state);
            var ev = new GeneActivatedEvent(id);
            RaiseLocalEvent(args.Target, ref ev);
        }

        RecalculateInstability(cloneGenome);
        RefreshVisuals(args.Target, cloneGenome);
        Dirty(args.Target, cloneGenome);
    }

    public GeneState GetOrCreateGene(Entity<GenomeComponent> ent, string geneId)
    {
        if (!ent.Comp.Genes.TryGetValue(geneId, out var state) || state == null)
        {
            state = new GeneState();
            ent.Comp.Genes[geneId] = state;
        }

        return state;
    }

    /// <summary>
    /// IPC, borgs, drones and other steel bodies have no genome to rewrite.
    /// </summary>
    public bool IsSteel(EntityUid uid)
    {
        if (HasComp<SiliconComponent>(uid) || HasComp<BorgChassisComponent>(uid) || _tag.HasTag(uid, "Bot"))
            return true;

        if (TryComp<DamageableComponent>(uid, out var damage) && damage.DamageContainerID == SteelDamageContainer)
            return true;

        return TryComp<HumanoidAppearanceComponent>(uid, out var appearance) && IsSteelSpecies(appearance.Species);
    }

    public bool IsSteelSpecies(ProtoId<SpeciesPrototype> speciesId)
    {
        if (!Prototypes.TryIndex(speciesId, out var species))
            return false;

        if (!Prototypes.TryIndex<EntityPrototype>(species.Prototype, out var mob))
            return false;

        if (mob.TryGetComponent<SiliconComponent>(out _, EntityManager.ComponentFactory))
            return true;

        return mob.TryGetComponent<DamageableComponent>(out var damage, EntityManager.ComponentFactory)
               && damage.DamageContainerID == SteelDamageContainer;
    }

    public bool CanMutate(EntityUid uid)
    {
        return !IsSteel(uid);
    }

    public bool TryActivateGene(EntityUid uid, string geneId, GenomeComponent? genome = null, bool requireComplete = true)
    {
        if (!CanMutate(uid) || !Resolve(uid, ref genome))
            return false;

        if (!Prototypes.TryIndex<GenePrototype>(geneId, out var proto))
            return false;

        var state = GetOrCreateGene((uid, genome), geneId);
        if (requireComplete && !IsAssembled(genome, proto))
            return false;

        if (state.Active)
            return true;

        foreach (var conflict in proto.Conflicts)
        {
            if (genome.Genes.TryGetValue(conflict, out var conflictState) && conflictState.Active)
                TryDeactivateGene(uid, conflict, genome);
        }

        DeactivateMorphConflicts(uid, genome, geneId, proto);

        state.Completion = 100;
        state.Active = true;
        state.Identified = true;
        state.SequenceLength = proto.SequenceLength;
        ApplyGeneComponents(uid, geneId, state);
        RecalculateInstability(genome);
        RefreshVisuals(uid, genome);
        Dirty(uid, genome);

        var ev = new GeneActivatedEvent(geneId);
        RaiseLocalEvent(uid, ref ev);
        MaybeGoHostile(GetMorphedBody(uid), proto);

        var alreadyKnown = IsDiscovered(geneId);
        Discover(geneId);
        if (alreadyKnown)
        {
            _popup.PopupEntity(Loc.GetString("genetics-gene-activated", ("gene", Loc.GetString(proto.Name))), uid);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("genetics-gene-expressed"), uid);
        }

        return true;
    }

    private void DeactivateMorphConflicts(EntityUid uid, GenomeComponent genome, string geneId, GenePrototype proto)
    {
        var isForm = proto.SpeciesTarget != null || proto.PolymorphPool.Count > 0;
        if (!isForm)
            return;

        foreach (var (id, state) in genome.Genes.ToList())
        {
            if (id == geneId || !state.Active)
                continue;

            if (!Prototypes.TryIndex<GenePrototype>(id, out var other))
                continue;

            if (other.SpeciesTarget != null || other.PolymorphPool.Count > 0)
                TryDeactivateGene(uid, id, genome);
        }
    }

    public bool TryInjectGene(EntityUid uid, string geneId, GenomeComponent? genome = null)
    {
        if (!Resolve(uid, ref genome))
            return false;

        var state = GetOrCreateGene((uid, genome), geneId);
        if (!Prototypes.TryIndex<GenePrototype>(geneId, out var proto))
            return false;

        WriteRecipeToBranch(genome, proto);
        state.Identified = true;
        state.Completion = 100;
        return TryActivateGene(uid, geneId, genome, requireComplete: false);
    }

    public bool TryDeactivateGene(EntityUid uid, string geneId, GenomeComponent? genome = null, bool requireAssembled = false)
    {
        if (!Resolve(uid, ref genome, false))
            return false;

        if (!genome.Genes.TryGetValue(geneId, out var state) || !state.Active)
            return false;

        if (!Prototypes.TryIndex<GenePrototype>(geneId, out var proto))
            return false;

        if (requireAssembled && !IsAssembled(genome, proto))
            return false;

        RemoveGeneComponents(uid, state);
        state.Active = false;
        RecalculateInstability(genome);
        RefreshVisuals(uid, genome);
        Dirty(uid, genome);

        RestoreAggression(uid, geneId);

        var ev = new GeneDeactivatedEvent(geneId);
        RaiseLocalEvent(uid, ref ev);

        if (IsDiscovered(geneId))
            _popup.PopupEntity(Loc.GetString("genetics-gene-deactivated", ("gene", Loc.GetString(proto.Name))), uid);
        else
            _popup.PopupEntity(Loc.GetString("genetics-gene-reverted"), uid);

        return true;
    }

    public bool TryRemoveGene(EntityUid uid, string geneId, GenomeComponent? genome = null)
    {
        if (!Resolve(uid, ref genome, false))
            return false;

        TryDeactivateGene(uid, geneId, genome);
        if (!genome.Genes.Remove(geneId))
            return false;

        RecalculateInstability(genome);
        RefreshVisuals(uid, genome);
        Dirty(uid, genome);
        return true;
    }

    public bool TryIdentifyGene(EntityUid uid, string geneId, GenomeComponent? genome = null)
    {
        if (!Resolve(uid, ref genome, false) || !Prototypes.TryIndex<GenePrototype>(geneId, out var proto))
            return false;

        EnsureBranches(genome);
        var matches = GetMatchCount(genome, proto);
        if (matches < 2)
            return false;

        var state = GetOrCreateGene((uid, genome), geneId);
        state.Identified = true;
        state.Completion = proto.SequenceLength <= 0 ? 0 : matches * 100 / proto.SequenceLength;
        Dirty(uid, genome);
        return true;
    }

    public void IdentifyClosest(EntityUid uid, GenomeComponent? genome = null)
    {
        if (!Resolve(uid, ref genome, false))
            return;

        EnsureBranches(genome);
        foreach (var proto in Prototypes.EnumeratePrototypes<GenePrototype>())
        {
            if (GetMatchCount(genome, proto) >= 2)
                TryIdentifyGene(uid, proto.ID, genome);
        }
    }

    public void IdentifyAll(EntityUid uid, GenomeComponent? genome = null)
    {
        IdentifyClosest(uid, genome);
    }

    public bool CanIrradiate(EntityUid uid)
    {
        return CanMutate(uid) && !_mobState.IsCritical(uid);
    }

    public void PulseBranchBlock(EntityUid uid, string geneId, int blockIndex, GenomeComponent? genome = null)
    {
        if (!CanMutate(uid))
        {
            _popup.PopupEntity(Loc.GetString("genetics-steel-no-mutate"), uid);
            return;
        }

        if (_mobState.IsCritical(uid))
        {
            _popup.PopupEntity(Loc.GetString("genetics-irradiate-critical"), uid);
            return;
        }
        GeneBranch branch;
        if (Enum.TryParse(geneId, out GeneBranch parsed))
            branch = parsed;
        else if (Prototypes.TryIndex<GenePrototype>(geneId, out var proto))
            branch = GetRoundBranch(proto.ID);
        else
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Radiation"] = 2;
        _damageable.TryChangeDamage(uid, damage, origin: uid);
        PulseBranchBlock(uid, branch, blockIndex, genome);
    }

    public void PulseBranchBlock(EntityUid uid, GeneBranch branch, int blockIndex, GenomeComponent? genome = null)
    {
        if (!CanMutate(uid) || !Resolve(uid, ref genome))
            return;

        EnsureBranches(genome);
        var strand = GetBranchSequence(genome, branch);
        if (blockIndex < 0 || blockIndex >= strand.Count)
            return;

        strand[blockIndex] = DnaSequence.NextCodon(strand[blockIndex]);
        ClearAssemblyHints(genome, branch);
        SyncGeneCompletions(uid, genome);
        Dirty(uid, genome);
    }

    public void MutateRandom(EntityUid uid, int completionDelta, bool activateOnComplete, GenomeComponent? genome = null)
    {
        if (!CanMutate(uid) || !Resolve(uid, ref genome))
            return;

        EnsureBranches(genome);
        var branch = _random.Pick(Enum.GetValues<GeneBranch>());
        var strand = GetBranchSequence(genome, branch);
        var index = _random.Next(strand.Count);
        strand[index] = _random.Prob(0.65f)
            ? DnaSequence.NextCodon(strand[index])
            : _random.Pick(DnaSequence.Codons);

        ClearAssemblyHints(genome, branch);
        SyncGeneCompletions(uid, genome);
        Dirty(uid, genome);

        if (!activateOnComplete)
            return;

        foreach (var geneId in EnumerateRoundGenes(branch))
        {
            if (!Prototypes.TryIndex<GenePrototype>(geneId, out var proto) || !IsAssembled(genome, proto))
                continue;

            TryActivateGene(uid, proto.ID, genome);
        }
    }

    private void SyncGeneCompletions(EntityUid uid, GenomeComponent genome)
    {
        foreach (var proto in Prototypes.EnumeratePrototypes<GenePrototype>())
        {
            if (!IsGeneInRound(proto.ID) || !genome.Genes.TryGetValue(proto.ID, out var state))
                continue;

            var matches = GetMatchCount(genome, proto);
            var length = Math.Max(proto.SequenceLength, 1);
            state.Completion = matches * 100 / length;
            state.SequenceLength = proto.SequenceLength;
        }
    }

    private bool TryPickRandomGene(GenomeComponent genome, out string geneId)
    {
        geneId = string.Empty;
        var options = new List<(string Id, float Weight)>();

        foreach (var proto in Prototypes.EnumeratePrototypes<GenePrototype>())
        {
            if (!IsGeneInRound(proto.ID))
                continue;

            if (proto.Unique && genome.Genes.TryGetValue(proto.ID, out var existing) && existing.Active)
                continue;

            options.Add((proto.ID, MathF.Max(proto.Weight, 0.01f)));
        }

        if (options.Count == 0)
            return false;

        var total = 0f;
        foreach (var option in options)
            total += option.Weight;

        var roll = _random.NextFloat() * total;
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll > 0f)
                continue;

            geneId = option.Id;
            return true;
        }

        geneId = options[^1].Id;
        return true;
    }

    private EntityUid GetMorphedBody(EntityUid uid)
    {
        var query = EntityQueryEnumerator<Content.Server.Polymorph.Components.PolymorphedEntityComponent>();
        while (query.MoveNext(out var child, out var poly))
        {
            if (poly.Parent == uid)
                return child;
        }

        return uid;
    }

    private void ApplyGeneComponents(EntityUid uid, string geneId, GeneState state)
    {
        if (!Prototypes.TryIndex<GenePrototype>(geneId, out var proto) || proto.Components == null)
            return;

        foreach (var (name, data) in proto.Components)
        {
            var newComp = (Component) Factory.GetComponent(name);
            if (HasComp(uid, newComp.GetType()))
                continue;

            object? temp = newComp;
            _serialization.CopyTo(data.Component, ref temp);
            EntityManager.AddComponent(uid, (Component) temp!);
            state.AppliedComponents.Add(name);
        }
    }

    private void RemoveGeneComponents(EntityUid uid, GeneState state)
    {
        foreach (var name in state.AppliedComponents)
        {
            if (Factory.TryGetRegistration(name, out var registration))
                RemComp(uid, registration.Type);
        }

        state.AppliedComponents.Clear();
    }

    public string GetUniqueDna(EntityUid uid)
    {
        return TryComp<DnaComponent>(uid, out var dna) ? dna.DNA : Loc.GetString("genetics-unknown-dna");
    }
}
