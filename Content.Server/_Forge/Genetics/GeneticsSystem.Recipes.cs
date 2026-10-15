using System.Linq;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.Genetics;

public sealed class GeneRoundPayload
{
    public GeneBranch Branch;
    public List<string> Recipe = new();
    public string? Marking;
    public ProtoId<PolymorphPrototype>? Polymorph;
    public ProtoId<SpeciesPrototype>? SpeciesForm;
    public ProtoId<SpeciesPrototype>? LimbSpecies;
    public HumanoidVisualLayers LimbRoot = HumanoidVisualLayers.LArm;
    public int RecipeOffset;
}

public sealed partial class GeneticsSystem
{
    private readonly Dictionary<string, GeneRoundPayload> _payloads = new();

    private void InitializeRecipes()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearRecipes());
        SubscribeLocalEvent<RoundStartedEvent>(_ => EnsureRecipes());
    }

    private void ClearRecipes()
    {
        _payloads.Clear();
    }

    public void EnsureRecipes()
    {
        if (_payloads.Count > 0)
            return;

        var all = Prototypes.EnumeratePrototypes<GenePrototype>().ToList();
        if (all.Count == 0)
            return;

        var locked = all.Where(proto => proto.LockBranch || proto.SpeciesTarget != null).ToList();
        var pool = all.Where(proto => !locked.Contains(proto)).ToList();
        _random.Shuffle(pool);
        var keep = Math.Clamp((int) MathF.Ceiling(pool.Count * 0.75f), Math.Min(8, pool.Count), pool.Count);
        var selected = pool.Count == 0 ? new List<GenePrototype>() : pool.GetRange(0, keep);
        foreach (var proto in locked)
        {
            if (!selected.Contains(proto))
                selected.Add(proto);
        }

        if (!selected.Any(IsMorphGene))
        {
            var morph = all.FirstOrDefault(IsMorphGene);
            if (morph != null)
                selected.Add(morph);
        }

        var branches = Enum.GetValues<GeneBranch>().Where(branch => branch != GeneBranch.Morphology).ToList();
        _random.Shuffle(branches);

        var used = new HashSet<string>();
        var usedRecipes = new List<(int Offset, List<string> Recipe)>();
        var shuffleIndex = 0;

        foreach (var proto in selected)
        {
            if (proto.SpeciesTarget != null && IsSteelSpecies(proto.SpeciesTarget.Value))
                continue;
            var length = Math.Clamp(proto.SequenceLength, 2, DnaSequence.BranchLength);
            var recipe = BuildUniqueRecipe(length, used, usedRecipes, out var offset);
            usedRecipes.Add((offset, recipe));

            var branch = proto.LockBranch || proto.SpeciesTarget != null
                ? proto.Branch
                : branches[shuffleIndex++ % branches.Count];

            _payloads[proto.ID] = BuildPayload(proto, branch, recipe, offset);
        }
    }

    private static bool IsMorphGene(GenePrototype proto)
    {
        return proto.PolymorphPool.Count > 0
               || proto.MarkingPool.Count > 0
               || proto.SpeciesForm
               || proto.SpeciesTarget != null
               || proto.ReplaceLimb;
    }

    private GeneRoundPayload BuildPayload(GenePrototype proto, GeneBranch branch, List<string> recipe, int offset)
    {
        var payload = new GeneRoundPayload
        {
            Branch = branch,
            Recipe = recipe,
            RecipeOffset = offset,
        };

        if (proto.MarkingPool.Count > 0)
            payload.Marking = _random.Pick(proto.MarkingPool);

        if (proto.PolymorphPool.Count > 0)
            payload.Polymorph = _random.Pick(proto.PolymorphPool);

        if (proto.SpeciesTarget != null)
            payload.SpeciesForm = proto.SpeciesTarget;
        else if (proto.SpeciesForm)
            payload.SpeciesForm = PickRoundStartSpecies();

        if (proto.ReplaceLimb)
        {
            payload.LimbRoot = _random.Pick(GraftRoots);
            payload.LimbSpecies = PickRoundStartSpecies(requireRoot: payload.LimbRoot);
        }

        return payload;
    }

    private static readonly HumanoidVisualLayers[] GraftRoots =
    [
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RLeg,
    ];

    private ProtoId<SpeciesPrototype>? PickRoundStartSpecies(string? exclude = null, HumanoidVisualLayers? requireRoot = null)
    {
        var options = new List<ProtoId<SpeciesPrototype>>();
        foreach (var species in Prototypes.EnumeratePrototypes<SpeciesPrototype>())
        {
            if (!species.RoundStart || species.ID == exclude || IsSteelSpecies(species.ID))
                continue;

            if (requireRoot != null && !HasGraftSprites(species, requireRoot.Value))
                continue;

            options.Add(species.ID);
        }

        if (options.Count == 0)
            return null;

        return _random.Pick(options);
    }

    private bool HasGraftSprites(SpeciesPrototype species, HumanoidVisualLayers root)
    {
        foreach (var layer in GetRequiredGraftLayers(root))
        {
            if (!TryGetLayerSprite(species, layer, Sex.Unsexed, out _))
                return false;
        }

        return true;
    }

    private static IEnumerable<HumanoidVisualLayers> GetRequiredGraftLayers(HumanoidVisualLayers root)
    {
        yield return root;

        switch (root)
        {
            case HumanoidVisualLayers.LArm:
                yield return HumanoidVisualLayers.LHand;
                break;
            case HumanoidVisualLayers.RArm:
                yield return HumanoidVisualLayers.RHand;
                break;
            case HumanoidVisualLayers.LLeg:
                yield return HumanoidVisualLayers.LFoot;
                break;
            case HumanoidVisualLayers.RLeg:
                yield return HumanoidVisualLayers.RFoot;
                break;
        }
    }

    private static IEnumerable<HumanoidVisualLayers> GetGraftLayers(HumanoidVisualLayers root)
    {
        foreach (var layer in GetRequiredGraftLayers(root))
            yield return layer;

        if (root == HumanoidVisualLayers.Head)
            yield return HumanoidVisualLayers.Eyes;
    }

    private bool TryGetLayerSprite(SpeciesPrototype species, HumanoidVisualLayers layer, Sex sex, out string id)
    {
        id = string.Empty;
        if (!Prototypes.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet, out var sprites))
            return false;

        if (!sprites.Sprites.TryGetValue(layer, out var spriteId))
            return false;

        var morphed = HumanoidVisualLayersExtension.GetSexMorph(layer, sex, spriteId);
        if (IsRealBaseSprite(morphed))
        {
            id = morphed;
            return true;
        }

        if (morphed != spriteId && IsRealBaseSprite(spriteId))
        {
            id = spriteId;
            return true;
        }

        return false;
    }

    private bool IsRealBaseSprite(string id)
    {
        return Prototypes.TryIndex<HumanoidSpeciesSpriteLayer>(id, out var layerProto) && layerProto.BaseSprite != null;
    }

    private List<string> BuildUniqueRecipe(
        int length,
        HashSet<string> used,
        List<(int Offset, List<string> Recipe)> existing,
        out int offset)
    {
        for (var attempt = 0; attempt < 192; attempt++)
        {
            var maxOffset = Math.Max(0, DnaSequence.BranchLength - length);
            var chosenOffset = _random.Next(maxOffset + 1);
            var recipe = DnaSequence.Random(_random, length);
            var key = $"{chosenOffset}:{DnaSequence.Format(recipe)}";
            if (!used.Add(key))
                continue;

            if (existing.Any(other => OccupancyContained(chosenOffset, recipe, other.Offset, other.Recipe) ||
                                      OccupancyContained(other.Offset, other.Recipe, chosenOffset, recipe)))
            {
                used.Remove(key);
                continue;
            }

            offset = chosenOffset;
            return recipe;
        }

        offset = 0;
        var fallback = DnaSequence.Random(_random, length);
        used.Add($"0:{DnaSequence.Format(fallback)}");
        return fallback;
    }

    private static bool OccupancyContained(int offsetA, IReadOnlyList<string> a, int offsetB, IReadOnlyList<string> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var pos = offsetA + i;
            var j = pos - offsetB;
            if (j < 0 || j >= b.Count || b[j] != a[i])
                return false;
        }

        return true;
    }

    public GeneRoundPayload GetOrCreatePayload(string geneId)
    {
        EnsureRecipes();
        if (_payloads.TryGetValue(geneId, out var payload))
            return payload;

        Prototypes.TryIndex<GenePrototype>(geneId, out var proto);
        var length = proto != null
            ? Math.Clamp(proto.SequenceLength, 2, DnaSequence.BranchLength)
            : 4;

        payload = proto != null
            ? BuildPayload(proto, proto.Branch, DnaSequence.Random(_random, length), 0)
            : new GeneRoundPayload
            {
                Branch = GeneBranch.Physical,
                Recipe = DnaSequence.Random(_random, length),
            };

        _payloads[geneId] = payload;
        return payload;
    }

    public bool IsGeneInRound(string geneId)
    {
        EnsureRecipes();
        return _payloads.ContainsKey(geneId);
    }

    public GeneBranch GetRoundBranch(string geneId)
    {
        return GetOrCreatePayload(geneId).Branch;
    }

    public IReadOnlyList<string> GetRecipe(string geneId)
    {
        return GetOrCreatePayload(geneId).Recipe;
    }

    public bool IsDiscovered(EntityUid context, string geneId)
    {
        return _servers.IsDiscovered(context, geneId);
    }

    public bool Discover(EntityUid context, string geneId)
    {
        return _servers.Discover(context, geneId);
    }

    public IEnumerable<string> EnumerateRoundGenes(GeneBranch branch)
    {
        EnsureRecipes();
        foreach (var (id, payload) in _payloads)
        {
            if (payload.Branch == branch)
                yield return id;
        }
    }

    public List<GeneticsConsoleDiscoveryEntry> GetDiscoveryJournal(EntityUid context)
    {
        EnsureRecipes();
        var list = new List<GeneticsConsoleDiscoveryEntry>();
        foreach (var id in _servers.GetDiscovered(context))
        {
            if (!Prototypes.TryIndex<GenePrototype>(id, out var proto))
                continue;

            var payload = GetOrCreatePayload(id);
            list.Add(new GeneticsConsoleDiscoveryEntry
            {
                GeneId = id,
                Name = Loc.GetString(proto.Name),
                Description = Loc.GetString(proto.Description),
                Quality = proto.Quality,
                Branch = payload.Branch,
                Sequence = DnaSequence.FormatOccupied(payload.RecipeOffset, payload.Recipe),
            });
        }

        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public int GetRecipeOffset(string geneId)
    {
        return GetOrCreatePayload(geneId).RecipeOffset;
    }

    public int GetMatchCount(GenomeComponent genome, GenePrototype proto)
    {
        var strand = GetBranchSequence(genome, GetRoundBranch(proto.ID));
        return DnaSequence.MatchCount(strand, GetRecipe(proto.ID), GetRecipeOffset(proto.ID));
    }

    public bool IsAssembled(GenomeComponent genome, GenePrototype proto)
    {
        var strand = GetBranchSequence(genome, GetRoundBranch(proto.ID));
        return DnaSequence.Matches(strand, GetRecipe(proto.ID), GetRecipeOffset(proto.ID));
    }

    public void WriteRecipeToBranch(GenomeComponent genome, GenePrototype proto)
    {
        var strand = GetBranchSequence(genome, GetRoundBranch(proto.ID));
        var recipe = GetRecipe(proto.ID);
        var offset = GetRecipeOffset(proto.ID);
        for (var i = 0; i < recipe.Count && offset + i < strand.Count; i++)
            strand[offset + i] = recipe[i];
    }

    public bool TryExpressBranch(EntityUid uid, GeneBranch branch, GenomeComponent? genome = null)
    {
        if (!Resolve(uid, ref genome))
            return false;

        EnsureBranches(genome);
        RevealAssembly(genome, branch);
        var matches = new List<GenePrototype>();
        foreach (var geneId in EnumerateRoundGenes(branch))
        {
            if (!Prototypes.TryIndex<GenePrototype>(geneId, out var proto))
                continue;

            if (!IsAssembled(genome, proto))
                continue;

            matches.Add(proto);
        }

        if (matches.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("genetics-express-nothing"), uid);
            return false;
        }

        matches.Sort((a, b) => b.SequenceLength.CompareTo(a.SequenceLength));
        var any = false;
        foreach (var proto in matches)
        {
            any |= TryActivateGene(uid, proto.ID, genome);
        }

        return any;
    }

    public void RevealAssembly(GenomeComponent genome, GeneBranch branch)
    {
        var strand = GetBranchSequence(genome, branch);
        var recipes = new List<(int Offset, IReadOnlyList<string> Codons)>();
        foreach (var geneId in EnumerateRoundGenes(branch))
            recipes.Add((GetRecipeOffset(geneId), GetRecipe(geneId)));

        var hints = DnaSequence.ComputeBlockHints(strand, recipes);
        genome.AssemblyHints[branch] = hints.Select(hint => (byte) hint).ToList();
    }

    public void ClearAssemblyHints(GenomeComponent genome, GeneBranch branch)
    {
        genome.AssemblyHints.Remove(branch);
    }

    public bool TryDeactivateBranch(EntityUid uid, GeneBranch branch, GenomeComponent? genome = null, bool requireAssembled = false)
    {
        if (!Resolve(uid, ref genome, false))
            return false;

        var any = false;
        foreach (var geneId in EnumerateRoundGenes(branch).ToList())
        {
            if (genome.Genes.TryGetValue(geneId, out var state) && state.Active)
                any |= TryDeactivateGene(uid, geneId, genome, requireAssembled);
        }

        return any;
    }

    public bool TryGetPrintableGene(EntityUid uid, GeneBranch branch, bool requireAssembled, out string geneId, GenomeComponent? genome = null)
    {
        geneId = string.Empty;
        if (!Resolve(uid, ref genome, false))
            return false;

        string? fallback = null;
        foreach (var id in EnumerateRoundGenes(branch))
        {
            if (!IsDiscovered(uid, id) || !Prototypes.TryIndex<GenePrototype>(id, out var proto))
                continue;

            fallback ??= id;
            var assembled = IsAssembled(genome, proto);
            var active = genome.Genes.TryGetValue(id, out var state) && state.Active;
            if (requireAssembled && !assembled)
                continue;

            if (active || assembled)
            {
                geneId = id;
                return true;
            }
        }

        if (fallback != null && !requireAssembled)
        {
            geneId = fallback;
            return true;
        }

        return false;
    }
}
