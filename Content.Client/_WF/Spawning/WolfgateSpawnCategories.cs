using System.Linq;
using System.Text;
using Content.Client._WF.Stylesheets;

namespace Content.Client._WF.Spawning;

/// <summary>
/// Top-level folders of the spawn menu, listed in the sidebar in declaration order.
/// </summary>
public enum WfSpawnGroup : byte
{
    Mobs,
    Weapons,
    Vehicles,
    Xenonids,
    Clothing,
    Engineering,
    Medical,
    FoodDrink,
    Machines,
    Structures,
    Devices,
    Fun,
    Science,
    Effects,
    Mapping,
    Misc,
}

/// <summary>
/// Leaf categories. Every spawnable prototype is classified into exactly one of these, so the counts in the
/// sidebar add up to the total. Declaration order is sidebar order within a group.
/// </summary>
public enum WfSpawnCategory : byte
{
    // Mobs
    Humanoids,
    Silicons,
    Animals,
    MobsOther,

    // Weapons
    MeleeWeapons,
    Guns,
    Ammunition,
    Explosives,
    Shields,

    // Vehicles
    VehiclesDrivable,
    VehicleParts,
    VehicleInteriors,
    VehicleScenery,

    // Xenonids
    XenoCastes,
    XenoHive,

    // Clothing
    ClothingHead,
    ClothingEyes,
    ClothingNeck,
    ClothingOuter,
    ClothingUniform,
    ClothingHands,
    ClothingShoes,
    ClothingBelt,
    ClothingBack,
    ClothingOther,

    // Engineering
    Tools,
    Materials,
    Parts,
    Power,
    Atmospherics,

    // Medical
    Medicine,
    Chemistry,
    BodyParts,

    // Food & drink
    Food,
    Drinks,
    Botany,

    // Machines
    Computers,
    Machines,
    Vending,
    Disposals,

    // Structures
    WallsWindows,
    Doors,
    Furniture,
    Lockers,
    Lights,
    Decoration,
    Signs,

    // Devices
    Identification,
    Communications,
    Instruments,
    DevicesOther,

    // Fun
    Toys,
    Games,
    Documents,

    // Science
    Anomalies,
    Research,

    // Effects
    Projectiles,
    Effects,

    // Mapping
    Markers,
    Spawners,
    MappingTools,
    DoNotMap,
    Debug,

    // Misc
    ItemsOther,
    StructuresOther,
    Uncategorised,
}

/// <summary>
/// Static description of the category tree: which group a leaf hangs under, what it is called and what colour
/// its group reads as.
/// </summary>
public static class WfSpawnCategories
{
    private static readonly (WfSpawnCategory Category, WfSpawnGroup Group)[] Table =
    {
        (WfSpawnCategory.Humanoids, WfSpawnGroup.Mobs),
        (WfSpawnCategory.Silicons, WfSpawnGroup.Mobs),
        (WfSpawnCategory.Animals, WfSpawnGroup.Mobs),
        (WfSpawnCategory.MobsOther, WfSpawnGroup.Mobs),

        (WfSpawnCategory.MeleeWeapons, WfSpawnGroup.Weapons),
        (WfSpawnCategory.Guns, WfSpawnGroup.Weapons),
        (WfSpawnCategory.Ammunition, WfSpawnGroup.Weapons),
        (WfSpawnCategory.Explosives, WfSpawnGroup.Weapons),
        (WfSpawnCategory.Shields, WfSpawnGroup.Weapons),

        (WfSpawnCategory.VehiclesDrivable, WfSpawnGroup.Vehicles),
        (WfSpawnCategory.VehicleParts, WfSpawnGroup.Vehicles),
        (WfSpawnCategory.VehicleInteriors, WfSpawnGroup.Vehicles),
        (WfSpawnCategory.VehicleScenery, WfSpawnGroup.Vehicles),

        (WfSpawnCategory.XenoCastes, WfSpawnGroup.Xenonids),
        (WfSpawnCategory.XenoHive, WfSpawnGroup.Xenonids),

        (WfSpawnCategory.ClothingHead, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingEyes, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingNeck, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingOuter, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingUniform, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingHands, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingShoes, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingBelt, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingBack, WfSpawnGroup.Clothing),
        (WfSpawnCategory.ClothingOther, WfSpawnGroup.Clothing),

        (WfSpawnCategory.Tools, WfSpawnGroup.Engineering),
        (WfSpawnCategory.Materials, WfSpawnGroup.Engineering),
        (WfSpawnCategory.Parts, WfSpawnGroup.Engineering),
        (WfSpawnCategory.Power, WfSpawnGroup.Engineering),
        (WfSpawnCategory.Atmospherics, WfSpawnGroup.Engineering),

        (WfSpawnCategory.Medicine, WfSpawnGroup.Medical),
        (WfSpawnCategory.Chemistry, WfSpawnGroup.Medical),
        (WfSpawnCategory.BodyParts, WfSpawnGroup.Medical),

        (WfSpawnCategory.Food, WfSpawnGroup.FoodDrink),
        (WfSpawnCategory.Drinks, WfSpawnGroup.FoodDrink),
        (WfSpawnCategory.Botany, WfSpawnGroup.FoodDrink),

        (WfSpawnCategory.Computers, WfSpawnGroup.Machines),
        (WfSpawnCategory.Machines, WfSpawnGroup.Machines),
        (WfSpawnCategory.Vending, WfSpawnGroup.Machines),
        (WfSpawnCategory.Disposals, WfSpawnGroup.Machines),

        (WfSpawnCategory.WallsWindows, WfSpawnGroup.Structures),
        (WfSpawnCategory.Doors, WfSpawnGroup.Structures),
        (WfSpawnCategory.Furniture, WfSpawnGroup.Structures),
        (WfSpawnCategory.Lockers, WfSpawnGroup.Structures),
        (WfSpawnCategory.Lights, WfSpawnGroup.Structures),
        (WfSpawnCategory.Decoration, WfSpawnGroup.Structures),
        (WfSpawnCategory.Signs, WfSpawnGroup.Structures),

        (WfSpawnCategory.Identification, WfSpawnGroup.Devices),
        (WfSpawnCategory.Communications, WfSpawnGroup.Devices),
        (WfSpawnCategory.Instruments, WfSpawnGroup.Devices),
        (WfSpawnCategory.DevicesOther, WfSpawnGroup.Devices),

        (WfSpawnCategory.Toys, WfSpawnGroup.Fun),
        (WfSpawnCategory.Games, WfSpawnGroup.Fun),
        (WfSpawnCategory.Documents, WfSpawnGroup.Fun),

        (WfSpawnCategory.Anomalies, WfSpawnGroup.Science),
        (WfSpawnCategory.Research, WfSpawnGroup.Science),

        (WfSpawnCategory.Projectiles, WfSpawnGroup.Effects),
        (WfSpawnCategory.Effects, WfSpawnGroup.Effects),

        (WfSpawnCategory.Markers, WfSpawnGroup.Mapping),
        (WfSpawnCategory.Spawners, WfSpawnGroup.Mapping),
        (WfSpawnCategory.MappingTools, WfSpawnGroup.Mapping),
        (WfSpawnCategory.DoNotMap, WfSpawnGroup.Mapping),
        (WfSpawnCategory.Debug, WfSpawnGroup.Mapping),

        (WfSpawnCategory.ItemsOther, WfSpawnGroup.Misc),
        (WfSpawnCategory.StructuresOther, WfSpawnGroup.Misc),
        (WfSpawnCategory.Uncategorised, WfSpawnGroup.Misc),
    };

    private static readonly WfSpawnGroup[] GroupOf = BuildGroupLookup();

    /// <summary>Groups in sidebar order, each with its leaves in sidebar order.</summary>
    public static readonly (WfSpawnGroup Group, WfSpawnCategory[] Categories)[] Tree =
        Table.GroupBy(e => e.Group)
            .OrderBy(g => (int) g.Key)
            .Select(g => (g.Key, g.Select(e => e.Category).ToArray()))
            .ToArray();

    /// <summary>The sidebar group a category sits under.</summary>
    public static WfSpawnGroup GroupFor(WfSpawnCategory category) => GroupOf[(int) category];

    /// <summary>Localized category name.</summary>
    public static string CategoryName(WfSpawnCategory category) =>
        Loc.GetString($"wf-spawn-category-{Kebab(category.ToString())}");

    /// <summary>Localized group name.</summary>
    public static string GroupName(WfSpawnGroup group) =>
        Loc.GetString($"wf-spawn-group-{Kebab(group.ToString())}");

    /// <summary>
    /// Each group reads as one colour across the sidebar stripe and the tile stripe, so a glance at the grid
    /// says what kind of thing you are looking at.
    /// </summary>
    public static Color GroupColor(WfSpawnGroup group, WolfgateSkin skin)
    {
        return group switch
        {
            WfSpawnGroup.Mobs => Color.FromHex("#6FD98A"),
            WfSpawnGroup.Weapons => skin.Danger,
            WfSpawnGroup.Vehicles => Color.FromHex("#8FA86B"),
            WfSpawnGroup.Xenonids => Color.FromHex("#A2D94B"),
            WfSpawnGroup.Clothing => Color.FromHex("#C79BFF"),
            WfSpawnGroup.Engineering => Color.FromHex("#FFC14D"),
            WfSpawnGroup.Medical => Color.FromHex("#7FC7FF"),
            WfSpawnGroup.FoodDrink => Color.FromHex("#FF9E6B"),
            WfSpawnGroup.Machines => Color.FromHex("#9FB4C7"),
            WfSpawnGroup.Structures => Color.FromHex("#B08A67"),
            WfSpawnGroup.Devices => skin.Accent,
            WfSpawnGroup.Fun => Color.FromHex("#FF8ED0"),
            WfSpawnGroup.Science => Color.FromHex("#B36BFF"),
            WfSpawnGroup.Effects => Color.FromHex("#7FE6E6"),
            WfSpawnGroup.Mapping => skin.Caution,
            _ => skin.TextMuted,
        };
    }

    private static WfSpawnGroup[] BuildGroupLookup()
    {
        var lookup = new WfSpawnGroup[Enum.GetValues<WfSpawnCategory>().Length];
        foreach (var (category, group) in Table)
        {
            lookup[(int) category] = group;
        }

        return lookup;
    }

    /// <summary>PascalCase enum name to the kebab-case tail of its locale id.</summary>
    private static string Kebab(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
