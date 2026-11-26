using System.Linq;
using Content.Client.Mapping;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Mapping;

/// <summary>
///     Holds grouped mapping palette data and resolves tab/search views.
/// </summary>
public sealed class MappingPaletteSession
{
    public readonly MappingPaletteMemory Memory;

    public List<MappingPrototype> HierarchyRoots { get; } = new();
    public List<MappingPrototype> EntityGroups { get; private set; } = new();
    public List<MappingPrototype> TileGroups { get; private set; } = new();
    public List<MappingPrototype> DecalGroups { get; private set; } = new();
    public List<MappingPrototype> Spawnable { get; } = new();
    public Dictionary<IPrototype, MappingPrototype> ByPrototype { get; } = new();

    public MappingPaletteSession(MappingPaletteMemory memory)
    {
        Memory = memory;
    }

    public void Reset()
    {
        HierarchyRoots.Clear();
        EntityGroups = new List<MappingPrototype>();
        TileGroups = new List<MappingPrototype>();
        DecalGroups = new List<MappingPrototype>();
        Spawnable.Clear();
        ByPrototype.Clear();
    }

    public void BuildGroups(IPrototypeManager prototypes)
    {
        Spawnable.Clear();
        Spawnable.AddRange(HierarchyRoots
            .SelectMany(CollectSpawnable)
            .Where(IsSpawnable)
            .Distinct());

        IndexSpawnable();
        GroupIndexed(prototypes);
    }

    public void LoadFromPrototypes(IPrototypeManager prototypes)
    {
        Reset();

        foreach (var entity in prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (entity.Abstract || entity.HideSpawnMenu)
                continue;

            var name = string.IsNullOrWhiteSpace(entity.EditorSuffix)
                ? entity.Name
                : string.Concat(entity.Name, " [", entity.EditorSuffix, "]");
            Spawnable.Add(new MappingPrototype(entity, name));
        }

        foreach (var tile in prototypes.EnumeratePrototypes<ContentTileDefinition>())
        {
            if (tile.Abstract)
                continue;

            Spawnable.Add(new MappingPrototype(tile, Loc.GetString(tile.Name)));
        }

        foreach (var decal in prototypes.EnumeratePrototypes<DecalPrototype>())
        {
            if (!decal.ShowMenu || decal.Abstract)
                continue;

            Spawnable.Add(new MappingPrototype(decal, decal.ID));
        }

        IndexSpawnable();
        GroupIndexed(prototypes);

        HierarchyRoots.Add(new MappingPrototype(null, Loc.GetString("mapping-entities")) { Children = EntityGroups });
        HierarchyRoots.Add(new MappingPrototype(null, Loc.GetString("mapping-tiles")) { Children = TileGroups });
        HierarchyRoots.Add(new MappingPrototype(null, Loc.GetString("mapping-decals")) { Children = DecalGroups });
    }

    public bool TryGet(IPrototype prototype, out MappingPrototype mapping)
    {
        return ByPrototype.TryGetValue(prototype, out mapping!);
    }

    private void IndexSpawnable()
    {
        ByPrototype.Clear();
        foreach (var mapping in Spawnable)
        {
            if (mapping.Prototype != null)
                ByPrototype.TryAdd(mapping.Prototype, mapping);
        }
    }

    private void GroupIndexed(IPrototypeManager prototypes)
    {

        EntityGroups = MappingPaletteCatalog.BuildGroups(
            Spawnable.Where(static m => m.Prototype is EntityPrototype),
            m => MappingPaletteCatalog.GetEntityGroup((EntityPrototype) m.Prototype!, prototypes));

        TileGroups = MappingPaletteCatalog.BuildGroups(
            Spawnable.Where(static m => m.Prototype is ContentTileDefinition),
            m => MappingPaletteCatalog.GetTileGroup(m.Prototype!.ID));

        DecalGroups = MappingPaletteCatalog.BuildGroups(
            Spawnable.Where(static m => m.Prototype is DecalPrototype),
            m => MappingPaletteCatalog.GetDecalGroup((DecalPrototype) m.Prototype!));
    }

    private static bool IsSpawnable(MappingPrototype mapping)
    {
        return mapping.Prototype switch
        {
            EntityPrototype entity => !entity.Abstract && !entity.HideSpawnMenu,
            ContentTileDefinition tile => !tile.Abstract,
            DecalPrototype decal => decal.ShowMenu && !decal.Abstract,
            _ => false
        };
    }

    public List<MappingPrototype> RootsFor(MappingPaletteTab tab)
    {
        return tab switch
        {
            MappingPaletteTab.Entities => EntityGroups,
            MappingPaletteTab.Tiles => TileGroups,
            MappingPaletteTab.Decals => DecalGroups,
            MappingPaletteTab.Favorites => Spawnable
                .Where(m => m.Prototype != null && Memory.IsFavorite(MappingPaletteCatalog.GetRef(m.Prototype)))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MappingPaletteTab.Recents => ResolveRecents(),
            _ => HierarchyRoots
        };
    }

    public List<MappingPrototype> Search(string query, MappingPaletteTab tab)
    {
        var source = tab switch
        {
            MappingPaletteTab.Entities => Spawnable.Where(static m => m.Prototype is EntityPrototype),
            MappingPaletteTab.Tiles => Spawnable.Where(static m => m.Prototype is ContentTileDefinition),
            MappingPaletteTab.Decals => Spawnable.Where(static m => m.Prototype is DecalPrototype),
            MappingPaletteTab.Favorites => Spawnable.Where(m =>
                m.Prototype != null && Memory.IsFavorite(MappingPaletteCatalog.GetRef(m.Prototype))),
            MappingPaletteTab.Recents => ResolveRecents(),
            _ => Spawnable
        };

        var matches = source
            .Where(m => MappingPaletteCatalog.MatchesSearch(m, query))
            .ToList();
        matches.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return matches;
    }

    public List<MappingPrototype> FlatFor(MappingPaletteTab tab)
    {
        return tab switch
        {
            MappingPaletteTab.Tiles => Spawnable.Where(static m => m.Prototype is ContentTileDefinition)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MappingPaletteTab.Decals => Spawnable.Where(static m => m.Prototype is DecalPrototype)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => RootsFor(tab)
        };
    }

    public List<MappingPrototype> ResolveRecents()
    {
        var list = new List<MappingPrototype>();
        foreach (var id in Memory.Recents)
        {
            var match = Spawnable.FirstOrDefault(m =>
                m.Prototype != null && MappingPaletteCatalog.GetRef(m.Prototype)?.Equals(id) == true);
            if (match != null)
                list.Add(match);
        }

        return list;
    }

    public bool IsFavorite(MappingPrototype mapping)
    {
        return mapping.Prototype != null && Memory.IsFavorite(MappingPaletteCatalog.GetRef(mapping.Prototype));
    }

    private static IEnumerable<MappingPrototype> CollectSpawnable(MappingPrototype root)
    {
        if (root.Prototype != null)
            yield return root;

        if (root.Children == null)
            yield break;

        foreach (var child in root.Children)
        {
            foreach (var nested in CollectSpawnable(child))
                yield return nested;
        }
    }
}
