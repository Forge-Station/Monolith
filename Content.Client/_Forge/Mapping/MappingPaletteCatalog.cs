using System.Linq;
using Content.Client.Mapping;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Mapping;

/// <summary>
///     Groups mapping prototypes into mapper-friendly buckets (walls with walls, guns with guns, ...).
/// </summary>
public static class MappingPaletteCatalog
{
    public const string OtherGroup = "other";

    public static string GetEntityGroup(EntityPrototype prototype, IPrototypeManager prototypes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return GetEntityGroup(prototype, prototypes, seen);
    }

    public static string GetTileGroup(string id)
    {
        if (id.Contains("Space", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Space", StringComparison.OrdinalIgnoreCase))
            return "space";

        if (ContainsAny(id, "Lattice", "Catwalk", "Plating"))
            return "lattice";

        if (ContainsAny(id, "Carpet"))
            return "carpet";

        if (ContainsAny(id, "Wood", "Bamboo"))
            return "wood";

        if (ContainsAny(id, "Grass", "Dirt", "Asteroid", "Cave", "Planet", "Desert", "Sand", "Rock", "Snow", "Ice", "Lava", "Water", "Basalt"))
            return "planet";

        if (ContainsAny(id, "Shuttle", "Mono", "Tech"))
            return "shuttle";

        if (ContainsAny(id, "Steel", "White", "Dark", "Kitchen", "Cafeteria", "Freezer", "Showroom", "Hydro", "Concrete", "Tile"))
            return "station";

        return OtherGroup;
    }

    public static string GetDecalGroup(DecalPrototype decal)
    {
        if (decal.Tags.Count == 0)
            return GroupFromId(decal.ID);

        var tag = decal.Tags[0];
        return string.IsNullOrWhiteSpace(tag) ? OtherGroup : tag.ToLowerInvariant();
    }

    public static List<MappingPrototype> BuildGroups(
        IEnumerable<MappingPrototype> items,
        Func<MappingPrototype, string> getGroupId)
    {
        var folders = new Dictionary<string, MappingPrototype>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item.Prototype == null)
                continue;

            var key = getGroupId(item);
            if (!folders.TryGetValue(key, out var folder))
            {
                folder = new MappingPrototype(null, GetGroupName(key))
                {
                    Children = new List<MappingPrototype>()
                };
                folders[key] = folder;
            }

            folder.Children!.Add(item);
            item.PaletteGroup = folder;
        }

        var list = folders.Values.ToList();
        list.Sort(static (a, b) =>
        {
            var aOther = string.Equals(a.Name, GetGroupName(OtherGroup), StringComparison.Ordinal);
            var bOther = string.Equals(b.Name, GetGroupName(OtherGroup), StringComparison.Ordinal);
            if (aOther != bOther)
                return aOther ? 1 : -1;

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var folder in list)
        {
            folder.Children?.Sort(static (a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        return list;
    }

    public static string GetGroupName(string key)
    {
        var locId = string.Concat("mapping-palette-group-", key);
        var localized = Loc.GetString(locId);
        if (!string.Equals(localized, locId, StringComparison.Ordinal))
            return localized;

        if (string.IsNullOrEmpty(key))
            return key;

        var chars = key.ToCharArray();
        chars[0] = char.ToUpperInvariant(chars[0]);
        return new string(chars);
    }

    public static bool MatchesSearch(MappingPrototype mapping, string query)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        if (mapping.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        if (mapping.Prototype?.ID.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (mapping.PaletteGroup?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }

    public static MappingPaletteKind? GetKind(IPrototype prototype)
    {
        return prototype switch
        {
            EntityPrototype => MappingPaletteKind.Entity,
            ContentTileDefinition => MappingPaletteKind.Tile,
            DecalPrototype => MappingPaletteKind.Decal,
            _ => null
        };
    }

    public static MappingPaletteRef? GetRef(IPrototype prototype)
    {
        var kind = GetKind(prototype);
        return kind == null ? null : new MappingPaletteRef(kind.Value, prototype.ID);
    }

    public static MappingPaletteTab TabFor(IPrototype prototype)
    {
        return prototype switch
        {
            EntityPrototype => MappingPaletteTab.Entities,
            ContentTileDefinition => MappingPaletteTab.Tiles,
            DecalPrototype => MappingPaletteTab.Decals,
            _ => MappingPaletteTab.Entities
        };
    }

    private static string GetEntityGroup(EntityPrototype prototype, IPrototypeManager prototypes, HashSet<string> seen)
    {
        if (!seen.Add(prototype.ID))
            return OtherGroup;

        var own = MatchEntity(prototype);
        if (own != OtherGroup)
            return own;

        if (prototype.Parents == null)
            return OtherGroup;

        foreach (var parentId in prototype.Parents)
        {
            if (!prototypes.TryIndex(parentId, out EntityPrototype? parent))
                continue;

            var inherited = GetEntityGroup(parent, prototypes, seen);
            if (inherited != OtherGroup)
                return inherited;
        }

        return OtherGroup;
    }

    private static string MatchEntity(EntityPrototype prototype)
    {
        var id = prototype.ID;
        var comps = prototype.Components;

        if (HasComp(comps, "SpawnPoint") ||
            HasComp(comps, "RandomSpawner") ||
            HasComp(comps, "ConditionalSpawner") ||
            HasComp(comps, "EntityTableSpawner") ||
            ContainsAny(id, "Spawner", "SpawnPoint", "Marker"))
            return "spawners";

        if (HasComp(comps, "MobState") ||
            HasComp(comps, "Ghost") ||
            HasComp(comps, "Mind") ||
            ContainsAny(id, "Mob", "BaseMob"))
            return "mobs";

        if (HasComp(comps, "Gun") ||
            HasComp(comps, "HitscanBatteryAmmoProvider") ||
            HasComp(comps, "BallisticAmmoProvider") ||
            HasComp(comps, "Ammo") ||
            HasComp(comps, "CartridgeAmmo") ||
            ContainsAny(id, "Weapon", "Gun", "Rifle", "Pistol", "Shotgun", "Launcher"))
            return "weapons";

        if (HasComp(comps, "MeleeWeapon") && HasComp(comps, "Item") &&
            ContainsAny(id, "Sword", "Knife", "Spear", "Axe", "Club", "Bat", "Machete", "Dagger"))
            return "weapons";

        if (HasComp(comps, "Clothing") || ContainsAny(id, "Clothing", "Headset", "PDA"))
            return "clothing";

        if (HasComp(comps, "Healing") ||
            HasComp(comps, "MedicalScanner") ||
            HasComp(comps, "HealthAnalyzer") ||
            ContainsAny(id, "Medkit", "Brutepack", "Ointment", "Syringe", "Hypospray", "Defibrillator", "Pill"))
            return "medical";

        if (HasComp(comps, "Food") ||
            HasComp(comps, "Drink") ||
            HasComp(comps, "IngestionBlocker") && ContainsAny(id, "Food", "Drink"))
            return "food";

        if (HasComp(comps, "Tool") ||
            HasComp(comps, "Welder") ||
            ContainsAny(id, "Crowbar", "Screwdriver", "Wrench", "Multitool", "Wirecutter", "Welder"))
            return "tools";

        if (HasComp(comps, "Stack") || ContainsAny(id, "Sheet", "Part", "Ingot", "Ore", "Material"))
            return "materials";

        if (IsWallId(id))
            return "walls";

        if (ContainsAny(id, "Window", "Grille") && !ContainsAny(id, "Windowed"))
            return ContainsAny(id, "Grille") ? "walls" : "windows";

        if (HasComp(comps, "Door") ||
            ContainsAny(id, "Airlock", "Windoor", "Firelock", "BlastDoor", "Shutters"))
            return "doors";

        if (ContainsAny(id, "Table") && !ContainsAny(id, "Tabletop"))
            return "tables";

        if (HasComp(comps, "Strap") && ContainsAny(id, "Chair", "Seat", "Stool", "Bench", "Bed", "Toilet"))
            return "seating";

        if (ContainsAny(id, "Chair", "Stool", "Bench", "Seat", "Bed", "Sofa"))
            return "seating";

        if (HasComp(comps, "EntityStorage") ||
            HasComp(comps, "Storage") ||
            ContainsAny(id, "Locker", "Crate", "Closet", "Cabinet", "OreBox", "Dumpster"))
            return "storage";

        if (HasComp(comps, "PoweredLight") ||
            HasComp(comps, "ExpendableLight") ||
            ContainsAny(id, "Light", "Lamp", "Lantern", "Floodlight"))
            return "lighting";

        if (HasComp(comps, "Apc") ||
            HasComp(comps, "Cable") ||
            HasComp(comps, "Smes") ||
            HasComp(comps, "Battery") && !HasComp(comps, "Item") ||
            ContainsAny(id, "APC", "SMES", "Substation", "Cable", "Power"))
            return "power";

        if (HasComp(comps, "Pipe") ||
            HasComp(comps, "GasPipe") ||
            HasComp(comps, "GasVentPump") ||
            HasComp(comps, "GasVentScrubber") ||
            ContainsAny(id, "Pipe", "Vent", "Scrubber", "Atmos", "PortableScrubber"))
            return "atmos";

        if (HasComp(comps, "Computer") ||
            HasComp(comps, "Machine") ||
            HasComp(comps, "Lathe") ||
            ContainsAny(id, "Computer", "Machine", "Console", "Lathe", "Autolathe", "Protolathe"))
            return "machines";

        if (ContainsAny(id, "Catwalk", "Lattice", "Floor", "Carpet") && HasComp(comps, "Clickable"))
            return "floors";

        if (HasComp(comps, "Sign") || ContainsAny(id, "Sign", "Poster", "Painting", "Banner"))
            return "signs";

        if (HasComp(comps, "Anchorable") && !HasComp(comps, "Item"))
            return "furniture";

        if (HasComp(comps, "Item"))
            return "items";

        return OtherGroup;
    }

    private static bool IsWallId(string id)
    {
        return ContainsAny(id, "Wall", "Girder", "Railing", "Fence") &&
               !ContainsAny(id, "Wallet", "Wallpaper", "Wallmount", "WallMount");
    }

    private static string GroupFromId(string id)
    {
        var idx = IndexOfSeparator(id);
        if (idx <= 0)
            return OtherGroup;

        var prefix = id.Substring(0, idx);
        return string.IsNullOrWhiteSpace(prefix) ? OtherGroup : prefix.ToLowerInvariant();
    }

    private static int IndexOfSeparator(string id)
    {
        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (c == '_' || c == '-')
                return i;
        }

        return -1;
    }

    private static bool HasComp(ComponentRegistry components, string name)
    {
        return components.ContainsKey(name);
    }

    private static bool ContainsAny(string id, params string[] parts)
    {
        foreach (var part in parts)
        {
            if (id.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
