using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Prototypes;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Derives what the silos accept from the recipes of every machine that can link to them.
/// </summary>
public abstract partial class SharedFabricationSiloSystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private TagSystem _tag = default!;

    private readonly HashSet<EntProtoId> _acceptedParts = new();
    private readonly HashSet<ProtoId<ReagentPrototype>> _acceptedReagents = new();

    /// <summary>
    /// Parts a parts silo accepts.
    /// </summary>
    public IReadOnlySet<EntProtoId> AcceptedParts => _acceptedParts;

    /// <summary>
    /// Reagents a chemical silo accepts.
    /// </summary>
    public IReadOnlySet<ProtoId<ReagentPrototype>> AcceptedReagents => _acceptedReagents;

    private void InitializeWhitelist()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        BuildWhitelist();
    }

    /// <summary>
    /// Whether a linkable machine has a recipe that uses this part.
    /// </summary>
    public bool IsAcceptedPart(EntProtoId prototype)
    {
        return _acceptedParts.Contains(prototype);
    }

    /// <summary>
    /// Whether an item is a part a parts silo accepts.
    /// </summary>
    public bool IsAcceptedPart(EntityUid item)
    {
        return MetaData(item).EntityPrototype?.ID is { } id && _acceptedParts.Contains(id);
    }

    /// <summary>
    /// Whether a linkable machine has a recipe that uses this reagent.
    /// </summary>
    public bool IsAcceptedReagent(ProtoId<ReagentPrototype> reagent)
    {
        return _acceptedReagents.Contains(reagent);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>() ||
            args.WasModified<LatheRecipePrototype>() ||
            args.WasModified<LatheRecipePackPrototype>())
            BuildWhitelist();
    }

    /// <summary>
    /// Collects the parts and reagents of every recipe a silo-linkable machine can make, and the parts any of them
    /// lists as its own.
    /// </summary>
    private void BuildWhitelist()
    {
        _acceptedParts.Clear();
        _acceptedReagents.Clear();

        var recipes = new HashSet<ProtoId<LatheRecipePrototype>>();
        var blueprintWhitelists = new List<EntityWhitelist>();
        var partWhitelists = new List<EntityWhitelist>();
        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract ||
                !proto.TryGetComponent<FabricationSiloClientComponent>(out var client, _factory))
                continue;

            if (client.Parts != null)
                partWhitelists.Add(client.Parts);

            if (!proto.TryGetComponent<LatheComponent>(out var lathe, _factory))
                continue;

            AddPackRecipes(recipes, lathe.StaticPacks);
            AddPackRecipes(recipes, lathe.DynamicPacks);
            if (proto.TryGetComponent<EmagLatheRecipesComponent>(out var emag, _factory))
            {
                AddPackRecipes(recipes, emag.EmagStaticPacks);
                AddPackRecipes(recipes, emag.EmagDynamicPacks);
            }

            if (proto.TryGetComponent<BlueprintReceiverComponent>(out var receiver, _factory))
                blueprintWhitelists.Add(receiver.Whitelist);
        }

        if (blueprintWhitelists.Count > 0 || partWhitelists.Count > 0)
        {
            foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract)
                    continue;

                if (proto.TryGetComponent<BlueprintComponent>(out var blueprint, _factory) &&
                    blueprintWhitelists.Any(whitelist => PassesWhitelist(proto, whitelist)))
                    recipes.UnionWith(blueprint.ProvidedRecipes);

                if (partWhitelists.Any(whitelist => PassesWhitelist(proto, whitelist)))
                    _acceptedParts.Add(proto.ID);
            }
        }

        foreach (var id in recipes)
        {
            if (!_prototype.TryIndex(id, out var recipe))
                continue;

            _acceptedParts.UnionWith(recipe.Entities.Keys);
            _acceptedReagents.UnionWith(recipe.Reagents.Keys);
        }
    }

    private void AddPackRecipes(HashSet<ProtoId<LatheRecipePrototype>> recipes,
        IEnumerable<ProtoId<LatheRecipePackPrototype>> packs)
    {
        foreach (var id in packs)
        {
            if (_prototype.TryIndex(id, out var pack))
                recipes.UnionWith(pack.Recipes);
        }
    }

    /// <summary>
    /// Checks a prototype's components and tags against a whitelist; other criteria count as a pass.
    /// </summary>
    private bool PassesWhitelist(EntityPrototype proto, EntityWhitelist whitelist)
    {
        var checks = new List<bool>();
        if (whitelist.Components != null)
        {
            foreach (var name in whitelist.Components)
            {
                checks.Add(proto.Components.ContainsKey(name));
            }
        }

        if (whitelist.Tags != null)
        {
            proto.TryGetComponent<TagComponent>(out var tags, _factory);
            foreach (var tag in whitelist.Tags)
            {
                checks.Add(tags != null && _tag.HasTag(tags, tag));
            }
        }

        if (checks.Count == 0)
            return true;

        return whitelist.RequireAll ? checks.All(pass => pass) : checks.Any(pass => pass);
    }
}
