using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Loadouts;

/// <summary>Section of the loadout rail a group is listed under, in display order.</summary>
public enum LoadoutCategory : byte
{
    Clothing,
    HeadFace,
    Bags,
    Weapons,
    Tools,
    Extras,
    Rank,
}

/// <summary>
/// Sorts loadout groups into rail sections from the gear they hand out, since a group carries no category of
/// its own. Groups whose items sit in storage or in hand are told apart by their name key instead.
/// </summary>
public static class LoadoutCategorizer
{
    private const string KindNone = "none";
    private const string KindInhand = "inhand";
    private const string KindStorage = "storage";

    /// <summary>Group name keys placed by hand, checked before the slot rule.</summary>
    public static readonly IReadOnlyDictionary<string, LoadoutCategory> NameCategories = new Dictionary<string, LoadoutCategory>
    {
        ["loadout-group-weapon"] = LoadoutCategory.Weapons,
        ["loadout-group-weapon-civilian"] = LoadoutCategory.Weapons,
        ["loadout-group-pistol"] = LoadoutCategory.Weapons,
        ["loadout-group-magazine"] = LoadoutCategory.Weapons,
        ["loadout-group-pistol-magazine"] = LoadoutCategory.Weapons,
        ["loadout-group-special-gun"] = LoadoutCategory.Weapons,
        ["loadout-group-contractor-armorplates"] = LoadoutCategory.Weapons,
        ["loadout-group-contractor-utility"] = LoadoutCategory.Tools,
        ["loadout-group-contractor-encryption-key"] = LoadoutCategory.Tools,
        ["loadout-group-contractor-cartridge"] = LoadoutCategory.Tools,
        ["loadout-group-contractor-implanter"] = LoadoutCategory.Tools,
        ["loadout-group-mieyo-products"] = LoadoutCategory.Tools,
        ["loadout-group-contractor-face"] = LoadoutCategory.HeadFace,
    };

    private static readonly Dictionary<string, LoadoutCategory> SlotCategories = new()
    {
        ["jumpsuit"] = LoadoutCategory.Clothing,
        ["outerClothing"] = LoadoutCategory.Clothing,
        ["gloves"] = LoadoutCategory.Clothing,
        ["shoes"] = LoadoutCategory.Clothing,
        ["head"] = LoadoutCategory.HeadFace,
        ["mask"] = LoadoutCategory.HeadFace,
        ["eyes"] = LoadoutCategory.HeadFace,
        ["ears"] = LoadoutCategory.HeadFace,
        ["neck"] = LoadoutCategory.HeadFace,
        ["balaclava"] = LoadoutCategory.HeadFace,
        ["back"] = LoadoutCategory.Bags,
        ["belt"] = LoadoutCategory.Bags,
        ["id"] = LoadoutCategory.Bags,
        ["wallet"] = LoadoutCategory.Bags,
        ["pocket1"] = LoadoutCategory.Bags,
        ["pocket2"] = LoadoutCategory.Bags,
        ["suitstorage"] = LoadoutCategory.Bags,
    };

    /// <summary>Locale key of a category's rail heading.</summary>
    public static string NameKey(LoadoutCategory category)
    {
        return category switch
        {
            LoadoutCategory.Clothing => "wf-loadout-category-clothing",
            LoadoutCategory.HeadFace => "wf-loadout-category-headface",
            LoadoutCategory.Bags => "wf-loadout-category-bags",
            LoadoutCategory.Weapons => "wf-loadout-category-weapons",
            LoadoutCategory.Tools => "wf-loadout-category-tools",
            LoadoutCategory.Rank => "wf-loadout-category-rank",
            _ => "wf-loadout-category-extras",
        };
    }

    /// <summary>The loadouts a group offers: its own, then one level of subgroups, each listed once.</summary>
    public static List<ProtoId<LoadoutPrototype>> Items(LoadoutGroupPrototype group, IPrototypeManager protoManager)
    {
        var items = new List<ProtoId<LoadoutPrototype>>(group.Loadouts.Count);
        var seen = new HashSet<ProtoId<LoadoutPrototype>>();

        foreach (var loadout in group.Loadouts)
        {
            if (seen.Add(loadout))
                items.Add(loadout);
        }

        foreach (var subgroupId in group.Subgroups)
        {
            if (!protoManager.TryIndex(subgroupId, out var subgroup))
                continue;

            foreach (var loadout in subgroup.Loadouts)
            {
                if (seen.Add(loadout))
                    items.Add(loadout);
            }
        }

        return items;
    }

    /// <summary>Picks the rail section for a group.</summary>
    public static LoadoutCategory Categorize(LoadoutGroupPrototype group, IPrototypeManager protoManager)
    {
        if (NameCategories.TryGetValue(group.Name.Id, out var named))
            return named;

        var counts = new Dictionary<string, int>();
        var total = 0;

        foreach (var id in Items(group, protoManager))
        {
            if (!protoManager.TryIndex(id, out var loadout))
                continue;

            var kind = Kind(loadout, protoManager);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            total++;
        }

        if (total == 0)
            return LoadoutCategory.Extras;

        if (counts.GetValueOrDefault(KindNone) == total)
            return LoadoutCategory.Rank;

        string? dominant = null;
        var best = 0;
        foreach (var (kind, count) in counts)
        {
            if (count <= best)
                continue;

            best = count;
            dominant = kind;
        }

        return dominant != null && SlotCategories.TryGetValue(dominant, out var category)
            ? category
            : LoadoutCategory.Extras;
    }

    /// <summary>What a loadout hands out: the first slot it equips, or whether it only fills hands or storage.</summary>
    public static string Kind(LoadoutPrototype loadout, IPrototypeManager protoManager)
    {
        IEquipmentLoadout gear = loadout;
        if (protoManager.TryIndex(loadout.StartingGear, out var startingGear))
            gear = startingGear;

        foreach (var slot in gear.Equipment.Keys)
        {
            return slot;
        }

        if (gear.Inhand.Count > 0)
            return KindInhand;

        foreach (var items in gear.Storage.Values)
        {
            if (items.Count > 0)
                return KindStorage;
        }

        return KindNone;
    }
}
