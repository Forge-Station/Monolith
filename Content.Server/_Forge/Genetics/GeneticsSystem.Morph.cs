using Content.Server._Forge.Genetics.Components;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;

    private void InitializeMorph()
    {
        SubscribeLocalEvent<GenomeComponent, GeneActivatedEvent>(OnGeneActivatedMorph);
        SubscribeLocalEvent<GenomeComponent, GeneDeactivatedEvent>(OnGeneDeactivatedMorph);
    }

    private void OnGeneActivatedMorph(Entity<GenomeComponent> ent, ref GeneActivatedEvent args)
    {
        ApplyMorphEffects(ent, args.GeneId);
    }

    private void OnGeneDeactivatedMorph(Entity<GenomeComponent> ent, ref GeneDeactivatedEvent args)
    {
        RemoveMorphEffects(ent, args.GeneId);
    }

    private void ApplyMorphEffects(EntityUid uid, string geneId)
    {
        var payload = GetOrCreatePayload(geneId);
        var target = uid;

        if (payload.Polymorph != null && !HasComp<PolymorphedEntityComponent>(uid))
        {
            var child = _polymorph.PolymorphEntity(uid, payload.Polymorph.Value);
            if (child != null)
            {
                TransferGenome(uid, child.Value, reapplyComponents: true);
                target = child.Value;
            }
        }

        ApplyMorphExtras(target, geneId);
    }

    private void RemoveMorphEffects(EntityUid uid, string geneId)
    {
        if (!TryComp<GeneticMorphComponent>(uid, out var morph))
            return;

        RestoreLimb(uid, geneId, morph);

        if (morph.Markings.Remove(geneId, out var marking) &&
            TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) &&
            Prototypes.TryIndex<MarkingPrototype>(marking, out var markingProto))
        {
            humanoid.MarkingSet.Remove(markingProto.MarkingCategory, marking);
            Dirty(uid, humanoid);
        }

        RestoreSpecies(uid, geneId, morph);

        var payload = GetOrCreatePayload(geneId);
        if (payload.Polymorph == null || !TryComp<PolymorphedEntityComponent>(uid, out var poly))
            return;

        var parent = poly.Parent;
        TransferGenome(uid, parent, reapplyComponents: false);
        _polymorph.Revert((uid, poly));

        if (!TryComp<GenomeComponent>(parent, out var parentGenome))
            return;

        foreach (var (id, state) in parentGenome.Genes)
        {
            if (!state.Active)
                continue;

            if (state.AppliedComponents.Count == 0)
                ApplyGeneComponents(parent, id, state);

            if (id != geneId)
                ApplyMorphExtras(parent, id);
        }

        RefreshVisuals(parent, parentGenome);
        Dirty(parent, parentGenome);
    }

    private void ApplyMorphExtras(EntityUid uid, string geneId)
    {
        var payload = GetOrCreatePayload(geneId);
        var morph = EnsureComp<GeneticMorphComponent>(uid);

        ApplySpeciesForm(uid, geneId, payload, morph);

        if (payload.Marking != null && !morph.Markings.ContainsKey(geneId))
        {
            _humanoid.AddMarking(uid, payload.Marking, forced: true);
            morph.Markings[geneId] = payload.Marking;
        }

        ApplyLimbReplacement(uid, geneId, payload, morph);
    }

    private void ApplySpeciesForm(EntityUid uid, string geneId, GeneRoundPayload payload, GeneticMorphComponent morph)
    {
        if (payload.SpeciesForm == null || morph.Species.ContainsKey(geneId))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        var target = payload.SpeciesForm.Value;
        if (IsSteelSpecies(target))
            return;
        if (humanoid.Species == target)
            return;

        if (!Prototypes.TryIndex(target, out SpeciesPrototype? species))
            return;

        morph.Species[geneId] = new GeneticSpeciesSnapshot
        {
            Species = humanoid.Species,
            SkinColor = humanoid.SkinColor,
            Markings = new MarkingSet(humanoid.MarkingSet),
        };

        _humanoid.SetSpecies(uid, target, false, humanoid);
        _humanoid.SetSkinColor(uid, SkinColor.ValidSkinTone(species.SkinColoration, humanoid.SkinColor), verify: false, humanoid: humanoid);
    }

    private void RestoreSpecies(EntityUid uid, string geneId, GeneticMorphComponent morph)
    {
        if (!morph.Species.Remove(geneId, out var snapshot))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        humanoid.Species = snapshot.Species;
        humanoid.MarkingSet = new MarkingSet(snapshot.Markings);
        _humanoid.SetSkinColor(uid, snapshot.SkinColor, verify: false, humanoid: humanoid);
    }

    private void ApplyLimbReplacement(EntityUid uid, string geneId, GeneRoundPayload payload, GeneticMorphComponent morph)
    {
        if (payload.LimbSpecies == null || morph.LimbLayers.ContainsKey(geneId))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        var speciesId = payload.LimbSpecies.Value;
        if (humanoid.Species == speciesId)
        {
            var fallback = PickRoundStartSpecies(humanoid.Species, payload.LimbRoot);
            if (fallback == null)
                return;

            speciesId = fallback.Value;
        }

        if (IsSteelSpecies(speciesId))
            return;

        if (!Prototypes.TryIndex(speciesId, out SpeciesPrototype? species))
            return;

        var previous = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo?>();

        foreach (var layer in GetGraftLayers(payload.LimbRoot))
        {
            if (!TryGetLayerSprite(species, layer, humanoid.Sex, out var spriteId))
                continue;

            previous[layer] = humanoid.CustomBaseLayers.TryGetValue(layer, out var info) ? info : null;
            _humanoid.SetBaseLayerId(uid, layer, spriteId, false, humanoid);
        }

        if (previous.Count == 0)
            return;

        morph.LimbLayers[geneId] = previous;
        Dirty(uid, humanoid);
    }

    private void RestoreLimb(EntityUid uid, string geneId, GeneticMorphComponent morph)
    {
        if (!morph.LimbLayers.Remove(geneId, out var previous))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        foreach (var (layer, info) in previous)
        {
            if (info == null)
                humanoid.CustomBaseLayers.Remove(layer);
            else
                humanoid.CustomBaseLayers[layer] = info.Value;
        }

        Dirty(uid, humanoid);
    }

    private void TransferGenome(EntityUid from, EntityUid to, bool reapplyComponents)
    {
        if (!TryComp<GenomeComponent>(from, out var source))
            return;

        var dest = EnsureComp<GenomeComponent>(to);
        dest.Genes = CloneGenes(source.Genes);
        dest.Branches = CloneBranches(source.Branches);
        dest.Instability = source.Instability;
        dest.InstabilityThreshold = source.InstabilityThreshold;
        dest.RadiationAccumulated = source.RadiationAccumulated;
        dest.RadiationThreshold = source.RadiationThreshold;

        if (!reapplyComponents)
        {
            Dirty(to, dest);
            return;
        }

        foreach (var state in dest.Genes.Values)
        {
            state.AppliedComponents.Clear();
        }

        foreach (var (id, state) in dest.Genes)
        {
            if (!state.Active)
                continue;

            ApplyGeneComponents(to, id, state);
        }

        RefreshVisuals(to, dest);
        Dirty(to, dest);
    }

    private static Dictionary<string, GeneState> CloneGenes(Dictionary<string, GeneState> source)
    {
        var dest = new Dictionary<string, GeneState>(source.Count);
        foreach (var (id, state) in source)
        {
            dest[id] = new GeneState
            {
                Active = state.Active,
                Identified = state.Identified,
                Completion = state.Completion,
                SequenceLength = state.SequenceLength,
                AppliedComponents = new List<string>(state.AppliedComponents),
            };
        }

        return dest;
    }

    private static Dictionary<GeneBranch, List<string>> CloneBranches(Dictionary<GeneBranch, List<string>> source)
    {
        var dest = new Dictionary<GeneBranch, List<string>>(source.Count);
        foreach (var (branch, strand) in source)
            dest[branch] = new List<string>(strand);

        return dest;
    }
}
