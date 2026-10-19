using System.Linq;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Blocking.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Access.Components;
using Content.Shared.Anomaly.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Disposal.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.PDA;
using Content.Shared.Paper;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.Projectiles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Tools.Components;
using Content.Shared.VendingMachines;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using ClientEntityStorageComponent = Content.Client.Storage.Components.EntityStorageComponent;
using ClientInstrumentComponent = Content.Client.Instruments.InstrumentComponent;

namespace Content.Client._WF.Spawning;

/// <summary>
/// Sorts every spawnable entity prototype into one <see cref="WfSpawnCategory"/>.
/// <para>
/// Server-only components are stripped from the client's prototypes, so the rules lean on the components that do
/// survive plus the prototype's inheritance chain and id. Rules are ordered and the first match wins, which keeps
/// the counts exact: a gun is a gun, not a gun and an item.
/// </para>
/// </summary>
public sealed class WolfgateSpawnClassifier
{
    /// <summary>Minimum melee damage before an item counts as a weapon rather than something you can swing.</summary>
    private const int MeleeDamageThreshold = 12;

    private readonly IPrototypeManager _proto;

    // Registered component names, resolved once. Null when the component does not exist on the client.
    private readonly string? _item;
    private readonly string? _clothing;
    private readonly string? _gun;
    private readonly string? _melee;
    private readonly string? _cartridge;
    private readonly string? _ballistic;
    private readonly string? _basicAmmo;
    private readonly string? _hitscanAmmo;
    private readonly string? _projectile;
    private readonly string? _explosive;
    private readonly string? _blocking;
    private readonly string? _mobState;
    private readonly string? _body;
    private readonly string? _humanoid;
    private readonly string? _borg;
    private readonly string? _aiCore;
    private readonly string? _aiHolder;
    private readonly string? _butcherable;
    private readonly string? _organ;
    private readonly string? _bodyPart;
    private readonly string? _injector;
    private readonly string? _reagentTank;
    private readonly string? _drink;
    private readonly string? _tool;
    private readonly string? _stack;
    private readonly string? _material;
    private readonly string? _powerCell;
    private readonly string? _battery;
    private readonly string? _gasTank;
    private readonly string? _idCard;
    private readonly string? _pda;
    private readonly string? _instrument;
    private readonly string? _paper;
    private readonly string? _anomaly;
    private readonly string? _door;
    private readonly string? _vending;
    private readonly string? _disposal;
    private readonly string? _entityStorage;
    private readonly string? _storage;
    private readonly string? _strap;
    private readonly string? _poweredLight;
    private readonly string? _lightBulb;
    private readonly string? _vehicle;

    /// <summary>Ancestor ids of the prototype being classified, reused between calls.</summary>
    private readonly List<string> _ancestors = new();

    public WolfgateSpawnClassifier(IPrototypeManager proto, IComponentFactory factory)
    {
        _proto = proto;

        _item = Name<ItemComponent>(factory);
        _clothing = Name<ClothingComponent>(factory);
        _gun = Name<GunComponent>(factory);
        _melee = Name<MeleeWeaponComponent>(factory);
        _cartridge = Name<CartridgeAmmoComponent>(factory);
        _ballistic = Name<BallisticAmmoProviderComponent>(factory);
        _basicAmmo = Name<BasicEntityAmmoProviderComponent>(factory);
        _hitscanAmmo = Name<HitscanBatteryAmmoProviderComponent>(factory);
        _projectile = Name<ProjectileComponent>(factory);
        _explosive = Name<ExplosiveComponent>(factory);
        _blocking = Name<BlockingComponent>(factory);
        _mobState = Name<MobStateComponent>(factory);
        _body = Name<Shared.Body.Components.BodyComponent>(factory);
        _humanoid = Name<HumanoidAppearanceComponent>(factory);
        _borg = Name<BorgChassisComponent>(factory);
        _aiCore = Name<StationAiCoreComponent>(factory);
        _aiHolder = Name<StationAiHolderComponent>(factory);
        _butcherable = Name<ButcherableComponent>(factory);
        _organ = Name<OrganComponent>(factory);
        _bodyPart = Name<BodyPartComponent>(factory);
        _injector = Name<InjectorComponent>(factory);
        _reagentTank = Name<ReagentTankComponent>(factory);
        _drink = Name<DrinkComponent>(factory);
        _tool = Name<ToolComponent>(factory);
        _stack = Name<StackComponent>(factory);
        _material = Name<MaterialComponent>(factory);
        _powerCell = Name<PowerCellComponent>(factory);
        _battery = Name<BatteryComponent>(factory);
        _gasTank = Name<GasTankComponent>(factory);
        _idCard = Name<IdCardComponent>(factory);
        _pda = Name<PdaComponent>(factory);
        _instrument = Name<ClientInstrumentComponent>(factory);
        _paper = Name<PaperComponent>(factory);
        _anomaly = Name<AnomalyComponent>(factory);
        _door = Name<DoorComponent>(factory);
        _vending = Name<VendingMachineComponent>(factory);
        _disposal = Name<DisposalUnitComponent>(factory);
        _entityStorage = Name<ClientEntityStorageComponent>(factory);
        _storage = Name<StorageComponent>(factory);
        _strap = Name<StrapComponent>(factory);
        _poweredLight = Name<PoweredLightComponent>(factory);
        _lightBulb = Name<LightBulbComponent>(factory);
        _vehicle = Name<RMCVehicleComponent>(factory);
    }

    /// <summary>The spawn menu category a prototype belongs in, judged from its components and parents.</summary>
    public WfSpawnCategory Classify(EntityPrototype proto)
    {
        var comps = proto.Components;
        var id = proto.ID;

        _ancestors.Clear();
        foreach (var (parent, _) in _proto.EnumerateAllParents<EntityPrototype>(id, includeSelf: true))
        {
            _ancestors.Add(parent);
        }

        // Mapping and debug junk first, so a spawner never lands in the category of what it spawns.
        if (HasCategory(proto, "DoNotMap"))
            return WfSpawnCategory.DoNotMap;
        if (HasCategory(proto, "Mapping"))
            return WfSpawnCategory.MappingTools;
        if (Lineage("Spawn"))
            return WfSpawnCategory.Spawners;
        if (Lineage("Marker") || Lineage("StationBeacon"))
            return WfSpawnCategory.Markers;
        if (Lineage("Debug") || Lineage("Admeme"))
            return WfSpawnCategory.Debug;

        // Everything the vehicles module adds, apart from its rounds and effects.
        if (HasCategory(proto, "WFVehicles") && !Has(comps, _projectile) && !Lineage("Effect"))
        {
            if (Has(comps, _vehicle) || id == "CMUCargoCarrier")
                return WfSpawnCategory.VehiclesDrivable;
            if (Lineage("RMCProp") || id == "RMCCarPileup")
                return WfSpawnCategory.VehicleScenery;

            return Has(comps, _item) ? WfSpawnCategory.VehicleParts : WfSpawnCategory.VehicleInteriors;
        }

        // Everything the xenonids module adds, apart from its rounds and effects.
        if (HasCategory(proto, "WFXenonids") && !Has(comps, _projectile) && !Lineage("Effect"))
            return Has(comps, _mobState) ? WfSpawnCategory.XenoCastes : WfSpawnCategory.XenoHive;

        // Mobs.
        if (Has(comps, _humanoid))
            return WfSpawnCategory.Humanoids;
        if (Has(comps, _borg) || Has(comps, _aiCore) || Has(comps, _aiHolder) || Lineage("BorgChassis"))
            return WfSpawnCategory.Silicons;
        if (Has(comps, _mobState) || Has(comps, _body) || Lineage("BaseMob"))
        {
            return Has(comps, _butcherable) || Lineage("BaseMobAnimal") || Lineage("SimpleMob")
                ? WfSpawnCategory.Animals
                : WfSpawnCategory.MobsOther;
        }

        if (Has(comps, _organ) || Has(comps, _bodyPart))
            return WfSpawnCategory.BodyParts;

        var item = Has(comps, _item);

        // Things with an unmistakable identity go before clothing and weapons, because most SS14 items carry a
        // MeleeWeapon and many carry a belt-slot Clothing purely so they can be swung or holstered.
        if (Has(comps, _idCard) || Has(comps, _pda))
            return WfSpawnCategory.Identification;
        if (Has(comps, _instrument))
            return WfSpawnCategory.Instruments;
        if (Has(comps, _paper) || Lineage("Book") || Lineage("Folder") || Lineage("Blueprint"))
            return WfSpawnCategory.Documents;
        if (Has(comps, _anomaly) || Lineage("Anomaly"))
            return WfSpawnCategory.Anomalies;
        if (Lineage("Artifact"))
            return WfSpawnCategory.Research;
        if (Lineage("Toy") || Lineage("Plushie") || Lineage("Figurine") || Lineage("Balloon"))
            return WfSpawnCategory.Toys;
        if (Lineage("Dice") || Lineage("TabletopPiece") || Lineage("BoardTabletop") || Lineage("BoardGame") ||
            Lineage("PlayingCard"))
        {
            return WfSpawnCategory.Games;
        }

        // Chemistry glassware before food and drink, since a beaker is also a drink container.
        if (Lineage("ChemicalBarrel") || Lineage("Beaker") || Lineage("ChemistryBottle") || Lineage("BaseJug") ||
            Lineage("Vial") || (item && Has(comps, _reagentTank)))
        {
            return WfSpawnCategory.Chemistry;
        }

        // Food and drink. Food is a server-only component, so the base prototypes carry the signal here.
        if (Lineage("FoodBase") || Lineage("BaseFood") || Lineage("FoodInjectable") || Lineage("FoodSequence"))
            return WfSpawnCategory.Food;
        if (Has(comps, _drink) || Lineage("DrinkBase"))
            return WfSpawnCategory.Drinks;
        if (Lineage("ProduceBase") || Lineage("SeedBase") || Lineage("SeedExtractor") || id.EndsWith("Seeds", StringComparison.Ordinal))
            return WfSpawnCategory.Botany;
        if (Lineage("Food"))
            return WfSpawnCategory.Food;

        // Medicine.
        if (item && (Has(comps, _injector) || Lineage("Pill") || Lineage("Medipen") || Lineage("Ointment") ||
                     Lineage("Brutepack") || Lineage("Autoinjector") || Lineage("Defibrillator") ||
                     Lineage("Medkit") || Lineage("FirstAid")))
        {
            return WfSpawnCategory.Medicine;
        }

        // Power cells before ammo, because a weapon cell reads as both.
        if (id.Contains("PowerCell", StringComparison.Ordinal))
            return WfSpawnCategory.Power;

        // Ranged weapons and ammo, which are unambiguous.
        if (Has(comps, _gun))
            return WfSpawnCategory.Guns;
        if (Has(comps, _cartridge) || Has(comps, _ballistic) || Has(comps, _basicAmmo) || Has(comps, _hitscanAmmo) ||
            Lineage("Magazine") || Lineage("AmmoBox") || Lineage("SpeedLoader") || Lineage("Cartridge"))
        {
            return WfSpawnCategory.Ammunition;
        }

        if (item && (Lineage("Grenade") || Lineage("Explosive") || Lineage("Dynamite") || Lineage("C4")))
            return WfSpawnCategory.Explosives;
        if (item && Has(comps, _blocking))
            return WfSpawnCategory.Shields;

        // Engineering.
        if (Has(comps, _stack) && Has(comps, _material))
            return WfSpawnCategory.Materials;
        if (Lineage("Circuitboard") || Lineage("MachineBoard") || Lineage("BaseElectronics") || Lineage("Capacitor") ||
            Lineage("Manipulator") || Lineage("MatterBin"))
        {
            return WfSpawnCategory.Parts;
        }

        if (Has(comps, _powerCell))
            return WfSpawnCategory.Power;

        if (Has(comps, _gasTank) || Lineage("GasCanister") || Lineage("GasPipe") || Lineage("GasVent") ||
            Lineage("GasFilter") || Lineage("GasMixer") || Lineage("GasThermo") || Lineage("GasPressure") ||
            Lineage("AirAlarm") || Lineage("AtmosFix") || Lineage("HeatExchanger") || Lineage("Scrubber"))
        {
            return WfSpawnCategory.Atmospherics;
        }

        // Tools before melee, since a wrench hits as hard as a club but is not one. Anything the prototype tree
        // calls a weapon stays a weapon even when it can pry.
        if (item && Has(comps, _tool) && !Lineage("Weapon") && !Lineage("Axe") && !Lineage("Sword") &&
            !Lineage("Katana") && !Lineage("Machete") && !Lineage("Spear") && !Lineage("BaseballBat"))
        {
            return WfSpawnCategory.Tools;
        }

        if (Lineage("Headset") || Lineage("EncryptionKey") || Lineage("RadioHandheld") || Lineage("HandheldRadio") ||
            Lineage("Telecomms") || id.Contains("Radio", StringComparison.Ordinal))
        {
            return WfSpawnCategory.Communications;
        }

        // Clothing, split by the slot it is worn in. Belt and suit-storage slots only mean "this can be clipped to
        // a belt", so they are not treated as clothing unless the thing is actually a belt.
        if (comps.TryGetValue(_clothing ?? string.Empty, out var clothingEntry) &&
            clothingEntry.Component is ClothingComponent clothing &&
            ClothingCategory(clothing.Slots) is { } clothingCategory &&
            (clothingCategory != WfSpawnCategory.ClothingBelt || Has(comps, _storage) || Lineage("Belt") ||
             Lineage("Holster")))
        {
            return clothingCategory;
        }

        // Melee last of the weapons: almost every item can be swung, so only ones that actually hurt count, and a
        // toolbox or weapon case is a container first.
        if (item && !Has(comps, _storage) && comps.TryGetValue(_melee ?? string.Empty, out var meleeEntry) &&
            meleeEntry.Component is MeleeWeaponComponent melee && melee.Damage.GetTotal() >= MeleeDamageThreshold)
        {
            return WfSpawnCategory.MeleeWeapons;
        }


        // Structures and machines. Item-gated so a wallet is never a wall.
        if (!item)
        {
            if (Has(comps, _door) || Lineage("Airlock"))
                return WfSpawnCategory.Doors;
            if (Has(comps, _vending))
                return WfSpawnCategory.Vending;
            if (Has(comps, _disposal) || Lineage("Disposal"))
                return WfSpawnCategory.Disposals;
            if (Has(comps, _entityStorage) || Lineage("Locker") || Lineage("Crate") || Lineage("Closet") ||
                Lineage("Wardrobe") || Lineage("SuitStorage"))
            {
                return WfSpawnCategory.Lockers;
            }

            if (Lineage("Computer") || Lineage("Console"))
                return WfSpawnCategory.Computers;
            if (Lineage("BaseMachine") || Lineage("Lathe") || Lineage("Generator") || Lineage("Server") ||
                Lineage("APC") || Lineage("Substation") || Lineage("SMES") || Lineage("Thruster") ||
                Lineage("Gyroscope"))
            {
                return WfSpawnCategory.Machines;
            }
            if (Lineage("BaseWall") || Lineage("Window") || Lineage("Grille") || Lineage("Girder"))
                return WfSpawnCategory.WallsWindows;
            if (Has(comps, _strap) || Has(comps, _storage) || Lineage("Table") || Lineage("Chair") ||
                Lineage("Rack") || Lineage("Bed"))
            {
                return WfSpawnCategory.Furniture;
            }

            if (Has(comps, _poweredLight) || Has(comps, _lightBulb) || Lineage("Lamp") || Lineage("Lantern"))
                return WfSpawnCategory.Lights;
            if (Lineage("Sign") || Lineage("Poster") || Lineage("Painting"))
                return WfSpawnCategory.Signs;
            if (Lineage("Carpet") || Lineage("Curtain") || Lineage("Banner") || Lineage("Statue") ||
                Lineage("Flora") || Lineage("Plant") || Lineage("Rug"))
            {
                return WfSpawnCategory.Decoration;
            }

            if (Lineage("Effect") || Lineage("Smoke") || Lineage("Foam") || Lineage("Flash"))
                return WfSpawnCategory.Effects;
        }

        if (Has(comps, _projectile))
            return WfSpawnCategory.Projectiles;

        // Last sweep for things that explode but name themselves nothing in particular, such as snap pops. It runs
        // this late because the component is also on gas tanks, jetpacks and anything else that ruptures, and those
        // read better as the atmospherics and clothing the earlier rules made them.
        if (item && Has(comps, _explosive))
            return WfSpawnCategory.Explosives;

        if (item)
            return WfSpawnCategory.ItemsOther;
        if (Lineage("BaseStructure"))
            return WfSpawnCategory.StructuresOther;

        return WfSpawnCategory.Uncategorised;
    }

    private static WfSpawnCategory ClothingCategory(SlotFlags slots)
    {
        if ((slots & SlotFlags.HEAD) != 0)
            return WfSpawnCategory.ClothingHead;
        if ((slots & (SlotFlags.EYES | SlotFlags.MASK)) != 0)
            return WfSpawnCategory.ClothingEyes;
        if ((slots & (SlotFlags.NECK | SlotFlags.EARS)) != 0)
            return WfSpawnCategory.ClothingNeck;
        if ((slots & SlotFlags.OUTERCLOTHING) != 0)
            return WfSpawnCategory.ClothingOuter;
        if ((slots & SlotFlags.INNERCLOTHING) != 0)
            return WfSpawnCategory.ClothingUniform;
        if ((slots & SlotFlags.GLOVES) != 0)
            return WfSpawnCategory.ClothingHands;
        if ((slots & SlotFlags.FEET) != 0)
            return WfSpawnCategory.ClothingShoes;
        if ((slots & (SlotFlags.BELT | SlotFlags.SUITSTORAGE)) != 0)
            return WfSpawnCategory.ClothingBelt;
        if ((slots & SlotFlags.BACK) != 0)
            return WfSpawnCategory.ClothingBack;

        return WfSpawnCategory.ClothingOther;
    }

    private static bool Has(ComponentRegistry comps, string? component) =>
        component != null && comps.ContainsKey(component);

    private static bool HasCategory(EntityPrototype proto, string category) =>
        proto.Categories.Any(c => c.ID == category);

    /// <summary>True when the prototype or any of its parents has the fragment in its id.</summary>
    private bool Lineage(string fragment)
    {
        foreach (var ancestor in _ancestors)
        {
            if (ancestor.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? Name<T>(IComponentFactory factory) where T : IComponent, new() =>
        factory.TryGetRegistration<T>(out var registration) ? registration.Name : null;
}
